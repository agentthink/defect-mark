from __future__ import annotations

import hashlib
import hmac
import json
import os
import secrets
import shutil
import sqlite3
import zipfile
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Any

import httpx
from fastapi import Depends, FastAPI, File, Form, Header, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
from pydantic import BaseModel, Field


DB_PATH = Path(os.getenv("MASKFLOW_DB", "/data/maskflow.sqlite3"))
STORAGE_ROOT = Path(os.getenv("MASKFLOW_STORAGE", "/data/storage"))
FREE_BYTES = int(os.getenv("MASKFLOW_FREE_BYTES", str(1024**3)))
SAM_SERVICE_URL = os.getenv("SAM_SERVICE_URL", "http://sam-service:8001").rstrip("/")
TOKEN_BYTES = 32

PLAN_LIMITS = {
    "free": {"daily_limit": 50, "quota_bytes": FREE_BYTES},
    "pro": {"daily_limit": 1000, "quota_bytes": 50 * 1024**3},
    "team": {"daily_limit": 100000, "quota_bytes": 500 * 1024**3},
}

app = FastAPI(title="MaskFlow API")
app.add_middleware(
    CORSMiddleware,
    allow_origins=os.getenv("MASKFLOW_CORS_ORIGINS", "*").split(","),
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def today_key() -> str:
    return date.today().isoformat()


def db() -> sqlite3.Connection:
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn


def ensure_column(conn: sqlite3.Connection, table: str, column: str, definition: str) -> None:
    columns = {row["name"] for row in conn.execute(f"PRAGMA table_info({table})").fetchall()}
    if column not in columns:
        conn.execute(f"ALTER TABLE {table} ADD COLUMN {column} {definition}")


def init_db() -> None:
    with db() as conn:
        conn.executescript(
            """
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                email TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                salt TEXT NOT NULL,
                plan TEXT NOT NULL DEFAULT 'free',
                quota_bytes INTEGER NOT NULL,
                used_bytes INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sessions (
                token TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                data_type TEXT NOT NULL DEFAULT 'detection',
                split_json TEXT NOT NULL,
                image_count INTEGER NOT NULL DEFAULT 0,
                annotation_count INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                project_id TEXT,
                name TEXT NOT NULL,
                path TEXT NOT NULL,
                size INTEGER NOT NULL,
                kind TEXT NOT NULL DEFAULT 'image',
                content_type TEXT,
                created_at TEXT NOT NULL,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS user_ai_quota (
                user_id INTEGER PRIMARY KEY,
                plan TEXT NOT NULL DEFAULT 'free',
                daily_limit INTEGER NOT NULL DEFAULT 50,
                daily_used INTEGER NOT NULL DEFAULT 0,
                daily_reset_at TEXT NOT NULL,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                job_id TEXT NOT NULL UNIQUE,
                type TEXT NOT NULL,
                title TEXT,
                project_id TEXT,
                file_id INTEGER,
                image_count INTEGER DEFAULT 1,
                status TEXT NOT NULL,
                progress REAL DEFAULT 0,
                result_json TEXT,
                error_message TEXT,
                created_at TEXT NOT NULL,
                started_at TEXT,
                finished_at TEXT,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS jobs (
                id TEXT PRIMARY KEY,
                app TEXT NOT NULL,
                type TEXT NOT NULL,
                user_id INTEGER NOT NULL,
                project_id TEXT,
                pool TEXT NOT NULL,
                priority TEXT NOT NULL,
                status TEXT NOT NULL,
                resources_json TEXT NOT NULL,
                input_json TEXT NOT NULL,
                output_json TEXT,
                params_json TEXT,
                node_id TEXT,
                reserved_credits INTEGER,
                charged_credits INTEGER,
                error TEXT,
                created_at TEXT NOT NULL,
                started_at TEXT,
                finished_at TEXT
            );
            CREATE TABLE IF NOT EXISTS job_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                payload_json TEXT,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS dataset_exports (
                id TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                project_id TEXT,
                task_id TEXT,
                status TEXT NOT NULL,
                path TEXT,
                size INTEGER NOT NULL DEFAULT 0,
                config_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                finished_at TEXT,
                error_message TEXT,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS nodes (
                id TEXT PRIMARY KEY,
                owner_id INTEGER NOT NULL,
                pool TEXT NOT NULL DEFAULT 'platform',
                status TEXT NOT NULL,
                gpu_model TEXT,
                vram_gb INTEGER,
                region TEXT,
                price_per_hour REAL,
                reputation REAL DEFAULT 1.0,
                api_key TEXT NOT NULL,
                created_at TEXT NOT NULL,
                approved_at TEXT,
                last_heartbeat TEXT
            );
            CREATE TABLE IF NOT EXISTS allocations (
                job_id TEXT PRIMARY KEY,
                node_id TEXT NOT NULL,
                gpu_index INTEGER NOT NULL DEFAULT 0,
                started_at TEXT NOT NULL,
                ended_at TEXT
            );
            CREATE TABLE IF NOT EXISTS pools (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                type TEXT NOT NULL,
                region TEXT,
                status TEXT NOT NULL DEFAULT 'active',
                capacity_json TEXT NOT NULL,
                policy_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS pricing_rules (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                resource_type TEXT NOT NULL,
                pool TEXT,
                region TEXT,
                unit_price REAL NOT NULL,
                billing_unit TEXT NOT NULL DEFAULT 'hour',
                status TEXT NOT NULL DEFAULT 'active',
                effective_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS wallet_ledger (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                delta INTEGER NOT NULL,
                reason TEXT NOT NULL,
                job_id TEXT,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS settlements (
                id TEXT PRIMARY KEY,
                provider_id INTEGER NOT NULL,
                period TEXT NOT NULL,
                node_count INTEGER NOT NULL DEFAULT 0,
                gross_amount REAL NOT NULL DEFAULT 0,
                platform_fee REAL NOT NULL DEFAULT 0,
                net_amount REAL NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'pending',
                created_at TEXT NOT NULL,
                paid_at TEXT
            );
            CREATE TABLE IF NOT EXISTS account_notification_settings (
                user_id INTEGER PRIMARY KEY,
                email_task INTEGER NOT NULL DEFAULT 1,
                email_billing INTEGER NOT NULL DEFAULT 1,
                browser_notice INTEGER NOT NULL DEFAULT 1,
                weekly_report INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS api_tokens (
                id TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                token_hash TEXT NOT NULL,
                token_prefix TEXT NOT NULL,
                created_at TEXT NOT NULL,
                last_used_at TEXT,
                revoked_at TEXT,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS team_members (
                id TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                email TEXT NOT NULL,
                role TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS account_devices (
                id TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                ip TEXT,
                user_agent TEXT,
                created_at TEXT NOT NULL,
                last_seen_at TEXT NOT NULL,
                revoked_at TEXT,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            """
        )
        ensure_column(conn, "users", "username", "TEXT")
        ensure_column(conn, "users", "phone", "TEXT")
        ensure_column(conn, "users", "avatar_path", "TEXT")
        ensure_column(conn, "files", "project_id", "TEXT")
        ensure_column(conn, "files", "kind", "TEXT NOT NULL DEFAULT 'image'")
        ensure_column(conn, "files", "content_type", "TEXT")
        seed_compute_defaults(conn)


@app.on_event("startup")
def startup() -> None:
    init_db()
    STORAGE_ROOT.mkdir(parents=True, exist_ok=True)


def hash_password(password: str, salt: str | None = None) -> tuple[str, str]:
    salt = salt or secrets.token_hex(16)
    digest = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), bytes.fromhex(salt), 150_000)
    return digest.hex(), salt


def verify_password(password: str, digest: str, salt: str) -> bool:
    candidate, _ = hash_password(password, salt)
    return hmac.compare_digest(candidate, digest)


def public_user(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "email": row["email"],
        "username": row["username"] or "MaskFlow User",
        "phone": row["phone"] or "",
        "avatarUrl": f"/api/account/avatar" if row["avatar_path"] else None,
        "plan": row["plan"],
        "quotaBytes": row["quota_bytes"],
        "usedBytes": row["used_bytes"],
        "freeBytes": max(0, row["quota_bytes"] - row["used_bytes"]),
    }


def notification_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "emailTask": bool(row["email_task"]),
        "emailBilling": bool(row["email_billing"]),
        "browserNotice": bool(row["browser_notice"]),
        "weeklyReport": bool(row["weekly_report"]),
        "updatedAt": row["updated_at"],
    }


def api_token_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "name": row["name"],
        "prefix": row["token_prefix"],
        "createdAt": row["created_at"],
        "lastUsedAt": row["last_used_at"],
        "revokedAt": row["revoked_at"],
    }


def team_member_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "email": row["email"],
        "role": row["role"],
        "status": row["status"],
        "createdAt": row["created_at"],
    }


def device_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "name": row["name"],
        "ip": row["ip"],
        "userAgent": row["user_agent"],
        "createdAt": row["created_at"],
        "lastSeenAt": row["last_seen_at"],
        "revokedAt": row["revoked_at"],
    }


def file_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "projectId": row["project_id"],
        "name": row["name"],
        "size": row["size"],
        "kind": row["kind"],
        "contentType": row["content_type"],
        "createdAt": row["created_at"],
        "downloadUrl": f"/api/files/{row['id']}/download",
    }


def project_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "name": row["name"],
        "description": row["description"],
        "dataType": row["data_type"],
        "split": json.loads(row["split_json"]),
        "imageCount": row["image_count"],
        "annotationCount": row["annotation_count"],
        "createdAt": row["created_at"],
        "updatedAt": row["updated_at"],
    }


def task_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "jobId": row["job_id"],
        "type": row["type"],
        "title": row["title"],
        "projectId": row["project_id"],
        "fileId": row["file_id"],
        "imageCount": row["image_count"],
        "status": row["status"],
        "progress": row["progress"],
        "result": json.loads(row["result_json"]) if row["result_json"] else None,
        "errorMessage": row["error_message"],
        "createdAt": row["created_at"],
        "startedAt": row["started_at"],
        "finishedAt": row["finished_at"],
    }


def export_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "projectId": row["project_id"],
        "taskId": row["task_id"],
        "status": row["status"],
        "size": row["size"],
        "config": json.loads(row["config_json"]),
        "createdAt": row["created_at"],
        "finishedAt": row["finished_at"],
        "errorMessage": row["error_message"],
        "downloadUrl": f"/api/export/{row['id']}/download" if row["path"] else None,
    }


def decode_json(value: str | None, fallback: Any = None) -> Any:
    if not value:
        return fallback
    try:
        return json.loads(value)
    except json.JSONDecodeError:
        return fallback


def compute_job_payload(row: sqlite3.Row) -> dict[str, Any]:
    data = dict(row)
    data["resources"] = decode_json(data.pop("resources_json", None), {})
    data["input"] = decode_json(data.pop("input_json", None), {})
    data["output"] = decode_json(data.pop("output_json", None), None)
    data["params"] = decode_json(data.pop("params_json", None), {})
    return data


def node_payload(row: sqlite3.Row) -> dict[str, Any]:
    data = dict(row)
    data.pop("api_key", None)
    return data


def pool_payload(row: sqlite3.Row) -> dict[str, Any]:
    data = dict(row)
    data["capacity"] = decode_json(data.pop("capacity_json", None), {})
    data["policy"] = decode_json(data.pop("policy_json", None), {})
    return data


def pricing_payload(row: sqlite3.Row) -> dict[str, Any]:
    return dict(row)


def settlement_payload(row: sqlite3.Row) -> dict[str, Any]:
    return dict(row)


def seed_compute_defaults(conn: sqlite3.Connection) -> None:
    ts = now_iso()
    if conn.execute("SELECT COUNT(*) AS count FROM pools").fetchone()["count"] == 0:
        pools = [
            ("platform-gpu", "平台 GPU 池", "platform-gpu", "cn-east", {"gpu": 8, "vramGb": 192}, {"priority": "balanced"}),
            ("cpu-general", "通用 CPU 池", "cpu-general", "cn-east", {"cpu": 64, "memoryGb": 256}, {"priority": "low"}),
            ("reserved-cn-east", "华东预留池", "reserved", "cn-east", {"gpu": 2, "vramGb": 48}, {"priority": "high"}),
        ]
        for pool_id, name, pool_type, region, capacity, policy in pools:
            conn.execute(
                """
                INSERT INTO pools(id, name, type, region, capacity_json, policy_json, created_at, updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    pool_id,
                    name,
                    pool_type,
                    region,
                    json.dumps(capacity, ensure_ascii=False),
                    json.dumps(policy, ensure_ascii=False),
                    ts,
                    ts,
                ),
            )
    if conn.execute("SELECT COUNT(*) AS count FROM pricing_rules").fetchone()["count"] == 0:
        conn.execute(
            """
            INSERT INTO pricing_rules(id, name, resource_type, pool, region, unit_price, billing_unit, effective_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            ("price_gpu_default", "默认 GPU 定价", "gpu", "platform-gpu", "cn-east", 2.8, "hour", ts, ts),
        )


def ensure_account_defaults(conn: sqlite3.Connection, user: sqlite3.Row) -> None:
    ts = now_iso()
    conn.execute(
        """
        INSERT OR IGNORE INTO account_notification_settings(user_id, email_task, email_billing, browser_notice, weekly_report, updated_at)
        VALUES (?, 1, 1, 1, 0, ?)
        """,
        (user["id"], ts),
    )
    conn.execute(
        """
        INSERT OR IGNORE INTO team_members(id, user_id, email, role, status, created_at)
        VALUES (?, ?, ?, 'owner', 'active', ?)
        """,
        (f"member_owner_{user['id']}", user["id"], user["email"], ts),
    )
    conn.execute(
        """
        INSERT OR IGNORE INTO account_devices(id, user_id, name, ip, user_agent, created_at, last_seen_at)
        VALUES (?, ?, '当前浏览器', '127.0.0.1', 'MaskFlow Web', ?, ?)
        """,
        (f"device_default_{user['id']}", user["id"], ts, ts),
    )


def user_from_token(token: str) -> sqlite3.Row | None:
    with db() as conn:
        return conn.execute(
            """
            SELECT users.* FROM sessions
            JOIN users ON users.id = sessions.user_id
            WHERE sessions.token = ?
            """,
            (token,),
        ).fetchone()


def current_user(authorization: str | None = Header(default=None)) -> sqlite3.Row:
    if not authorization or not authorization.lower().startswith("bearer "):
        raise HTTPException(status_code=401, detail="Missing bearer token.")
    row = user_from_token(authorization.split(" ", 1)[1].strip())
    if row is None:
        raise HTTPException(status_code=401, detail="Invalid token.")
    return row


def optional_user(authorization: str | None = Header(default=None)) -> sqlite3.Row | None:
    if not authorization or not authorization.lower().startswith("bearer "):
        return None
    return user_from_token(authorization.split(" ", 1)[1].strip())


def ensure_quota_row(conn: sqlite3.Connection, user: sqlite3.Row) -> sqlite3.Row:
    limits = PLAN_LIMITS.get(user["plan"], PLAN_LIMITS["free"])
    row = conn.execute("SELECT * FROM user_ai_quota WHERE user_id = ?", (user["id"],)).fetchone()
    if row is None:
        conn.execute(
            "INSERT INTO user_ai_quota(user_id, plan, daily_limit, daily_used, daily_reset_at) VALUES (?, ?, ?, 0, ?)",
            (user["id"], user["plan"], limits["daily_limit"], today_key()),
        )
        row = conn.execute("SELECT * FROM user_ai_quota WHERE user_id = ?", (user["id"],)).fetchone()
    elif row["daily_reset_at"] != today_key() or row["plan"] != user["plan"]:
        conn.execute(
            "UPDATE user_ai_quota SET plan = ?, daily_limit = ?, daily_used = 0, daily_reset_at = ? WHERE user_id = ?",
            (user["plan"], limits["daily_limit"], today_key(), user["id"]),
        )
        row = conn.execute("SELECT * FROM user_ai_quota WHERE user_id = ?", (user["id"],)).fetchone()
    return row


def consume_ai_quota(user: sqlite3.Row | None, amount: int) -> dict[str, Any] | None:
    if user is None:
        return None
    with db() as conn:
        quota = ensure_quota_row(conn, user)
        if quota["daily_used"] + amount > quota["daily_limit"]:
            raise HTTPException(status_code=429, detail="今日 AI 处理次数已用完")
        conn.execute(
            "UPDATE user_ai_quota SET daily_used = daily_used + ? WHERE user_id = ?",
            (amount, user["id"]),
        )
        quota = conn.execute("SELECT * FROM user_ai_quota WHERE user_id = ?", (user["id"],)).fetchone()
        return quota_payload(quota)


def quota_payload(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "plan": row["plan"],
        "dailyLimit": row["daily_limit"],
        "dailyUsed": row["daily_used"],
        "dailyRemaining": max(0, row["daily_limit"] - row["daily_used"]),
        "dailyResetAt": row["daily_reset_at"],
    }


def model_json(model: BaseModel) -> str:
    if hasattr(model, "model_dump"):
        return json.dumps(model.model_dump(), ensure_ascii=False)
    return model.json()


def create_job_and_task(
    conn: sqlite3.Connection,
    user: sqlite3.Row,
    task_type: str,
    title: str,
    image_count: int = 1,
    project_id: str | None = None,
    file_id: int | None = None,
    job_type: str | None = None,
    params: dict[str, Any] | None = None,
    status: str = "running",
) -> tuple[str, str]:
    task_id = f"task_{secrets.token_hex(10)}"
    job_id = f"job_{secrets.token_hex(10)}"
    created = now_iso()
    conn.execute(
        """
        INSERT INTO jobs(id, app, type, user_id, project_id, pool, priority, status, resources_json, input_json,
                         output_json, params_json, reserved_credits, created_at, started_at)
        VALUES (?, 'maskflow', ?, ?, ?, 'platform', ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            job_id,
            job_type or task_type,
            user["id"],
            project_id,
            user["plan"],
            "running" if status == "running" else status,
            json.dumps({"gpu": 1, "vramGbMin": 8, "timeoutSec": 600}, ensure_ascii=False),
            json.dumps({"fileId": file_id, "imageCount": image_count}, ensure_ascii=False),
            json.dumps({"prefixUri": f"file://{STORAGE_ROOT / str(user['id']) / 'outputs' / job_id}"}, ensure_ascii=False),
            json.dumps(params or {}, ensure_ascii=False),
            image_count * 10,
            created,
            created if status == "running" else None,
        ),
    )
    conn.execute(
        """
        INSERT INTO tasks(id, user_id, job_id, type, title, project_id, file_id, image_count, status, progress, created_at, started_at)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            task_id,
            user["id"],
            job_id,
            task_type,
            title,
            project_id,
            file_id,
            image_count,
            status,
            0.1 if status == "running" else 0,
            created,
            created if status == "running" else None,
        ),
    )
    conn.execute(
        "INSERT INTO job_events(job_id, event_type, payload_json, created_at) VALUES (?, ?, ?, ?)",
        (job_id, "created", json.dumps({"taskId": task_id}, ensure_ascii=False), created),
    )
    return task_id, job_id


def finish_task(task_id: str, job_id: str, result: dict[str, Any] | None = None, error: str | None = None) -> None:
    finished = now_iso()
    status = "failed" if error else "succeeded"
    task_status = "failed" if error else "completed"
    with db() as conn:
        conn.execute(
            """
            UPDATE tasks SET status = ?, progress = ?, result_json = ?, error_message = ?, finished_at = ?
            WHERE id = ?
            """,
            (task_status, 1 if not error else 0, json.dumps(result, ensure_ascii=False) if result else None, error, finished, task_id),
        )
        conn.execute(
            "UPDATE jobs SET status = ?, charged_credits = reserved_credits, error = ?, finished_at = ? WHERE id = ?",
            (status, error, finished, job_id),
        )
        conn.execute(
            "INSERT INTO job_events(job_id, event_type, payload_json, created_at) VALUES (?, ?, ?, ?)",
            (job_id, status, json.dumps({"error": error}, ensure_ascii=False) if error else None, finished),
        )


@app.get("/")
def index() -> dict[str, str]:
    return {"name": "MaskFlow API", "samService": SAM_SERVICE_URL}


@app.get("/api/status")
async def status() -> dict[str, Any]:
    async with httpx.AsyncClient(timeout=30) as client:
        response = await client.get(f"{SAM_SERVICE_URL}/api/status")
        response.raise_for_status()
        sam = response.json()
    return {"api": "ok", **sam}


@app.post("/api/auth/register")
def register(email: str = Form(...), password: str = Form(...)) -> dict[str, Any]:
    email = email.strip().lower()
    if len(password) < 8:
        raise HTTPException(status_code=400, detail="密码至少需要 8 位")
    password_hash, salt = hash_password(password)
    try:
        with db() as conn:
            cur = conn.execute(
                "INSERT INTO users(email, password_hash, salt, quota_bytes, created_at) VALUES (?, ?, ?, ?, ?)",
                (email, password_hash, salt, FREE_BYTES, now_iso()),
            )
            user_id = cur.lastrowid
            token = secrets.token_urlsafe(TOKEN_BYTES)
            conn.execute("INSERT INTO sessions(token, user_id, created_at) VALUES (?, ?, ?)", (token, user_id, now_iso()))
            user = conn.execute("SELECT * FROM users WHERE id = ?", (user_id,)).fetchone()
            ensure_quota_row(conn, user)
            ensure_account_defaults(conn, user)
    except sqlite3.IntegrityError as exc:
        raise HTTPException(status_code=409, detail="邮箱已存在") from exc
    return {"token": token, "user": public_user(user)}


@app.post("/api/auth/login")
def login(email: str = Form(...), password: str = Form(...)) -> dict[str, Any]:
    with db() as conn:
        user = conn.execute("SELECT * FROM users WHERE email = ?", (email.strip().lower(),)).fetchone()
        if user is None or not verify_password(password, user["password_hash"], user["salt"]):
            raise HTTPException(status_code=401, detail="邮箱或密码错误")
        token = secrets.token_urlsafe(TOKEN_BYTES)
        conn.execute("INSERT INTO sessions(token, user_id, created_at) VALUES (?, ?, ?)", (token, user["id"], now_iso()))
        ensure_quota_row(conn, user)
        ensure_account_defaults(conn, user)
    return {"token": token, "user": public_user(user)}


@app.get("/api/me")
def me(user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        ensure_account_defaults(conn, user)
        quota = ensure_quota_row(conn, user)
    return {"user": public_user(user), "quota": quota_payload(quota)}


class ProfileUpdate(BaseModel):
    username: str = Field(min_length=1, max_length=80)
    phone: str = Field(default="", max_length=40)


class PasswordUpdate(BaseModel):
    currentPassword: str
    newPassword: str = Field(min_length=8)


class NotificationUpdate(BaseModel):
    emailTask: bool = True
    emailBilling: bool = True
    browserNotice: bool = True
    weeklyReport: bool = False


class ApiTokenCreate(BaseModel):
    name: str = Field(min_length=1, max_length=80)


class TeamMemberCreate(BaseModel):
    email: str
    role: str = "member"


@app.get("/api/account/profile")
def get_account_profile(user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        ensure_account_defaults(conn, user)
        fresh = conn.execute("SELECT * FROM users WHERE id = ?", (user["id"],)).fetchone()
        notifications = conn.execute(
            "SELECT * FROM account_notification_settings WHERE user_id = ?",
            (user["id"],),
        ).fetchone()
    return {"profile": public_user(fresh), "notifications": notification_payload(notifications)}


@app.put("/api/account/profile")
def update_account_profile(payload: ProfileUpdate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        conn.execute(
            "UPDATE users SET username = ?, phone = ? WHERE id = ?",
            (payload.username.strip(), payload.phone.strip(), user["id"]),
        )
        fresh = conn.execute("SELECT * FROM users WHERE id = ?", (user["id"],)).fetchone()
    return {"profile": public_user(fresh)}


@app.get("/api/account/avatar")
def get_account_avatar(user: sqlite3.Row = Depends(current_user)) -> FileResponse:
    with db() as conn:
        fresh = conn.execute("SELECT * FROM users WHERE id = ?", (user["id"],)).fetchone()
    if not fresh["avatar_path"]:
        raise HTTPException(status_code=404, detail="头像不存在")
    path = Path(fresh["avatar_path"])
    if not path.exists():
        raise HTTPException(status_code=404, detail="头像文件不存在")
    return FileResponse(path)


@app.post("/api/account/avatar")
async def upload_account_avatar(file: UploadFile = File(...), user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    if file.content_type not in {"image/jpeg", "image/png", "image/webp"}:
        raise HTTPException(status_code=400, detail="仅支持 JPG、PNG、WEBP 头像")
    data = await file.read()
    if len(data) > 5 * 1024 * 1024:
        raise HTTPException(status_code=400, detail="头像不能超过 5MB")
    suffix = Path(file.filename or "avatar.png").suffix.lower() or ".png"
    avatar_dir = STORAGE_ROOT / str(user["id"]) / "account"
    avatar_dir.mkdir(parents=True, exist_ok=True)
    path = avatar_dir / f"avatar{suffix}"
    path.write_bytes(data)
    with db() as conn:
        conn.execute("UPDATE users SET avatar_path = ? WHERE id = ?", (str(path), user["id"]))
        fresh = conn.execute("SELECT * FROM users WHERE id = ?", (user["id"],)).fetchone()
    return {"profile": public_user(fresh)}


@app.post("/api/account/password")
def update_account_password(payload: PasswordUpdate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    if not verify_password(payload.currentPassword, user["password_hash"], user["salt"]):
        raise HTTPException(status_code=400, detail="当前密码不正确")
    password_hash, salt = hash_password(payload.newPassword)
    with db() as conn:
        conn.execute(
            "UPDATE users SET password_hash = ?, salt = ? WHERE id = ?",
            (password_hash, salt, user["id"]),
        )
    return {"ok": True}


@app.get("/api/account/notifications")
def get_account_notifications(user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        ensure_account_defaults(conn, user)
        row = conn.execute("SELECT * FROM account_notification_settings WHERE user_id = ?", (user["id"],)).fetchone()
    return {"notifications": notification_payload(row)}


@app.put("/api/account/notifications")
def update_account_notifications(payload: NotificationUpdate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        ensure_account_defaults(conn, user)
        conn.execute(
            """
            UPDATE account_notification_settings
            SET email_task = ?, email_billing = ?, browser_notice = ?, weekly_report = ?, updated_at = ?
            WHERE user_id = ?
            """,
            (
                int(payload.emailTask),
                int(payload.emailBilling),
                int(payload.browserNotice),
                int(payload.weeklyReport),
                now_iso(),
                user["id"],
            ),
        )
        row = conn.execute("SELECT * FROM account_notification_settings WHERE user_id = ?", (user["id"],)).fetchone()
    return {"notifications": notification_payload(row)}


@app.get("/api/account/api-tokens")
def list_account_api_tokens(user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        rows = conn.execute(
            "SELECT * FROM api_tokens WHERE user_id = ? ORDER BY created_at DESC",
            (user["id"],),
        ).fetchall()
    return {"tokens": [api_token_payload(row) for row in rows]}


@app.post("/api/account/api-tokens")
def create_account_api_token(payload: ApiTokenCreate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    raw_token = f"mf_{secrets.token_urlsafe(32)}"
    token_hash, _ = hash_password(raw_token, secrets.token_hex(16))
    token_id = f"tok_{secrets.token_hex(8)}"
    with db() as conn:
        conn.execute(
            """
            INSERT INTO api_tokens(id, user_id, name, token_hash, token_prefix, created_at)
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            (token_id, user["id"], payload.name.strip(), token_hash, raw_token[:10], now_iso()),
        )
        row = conn.execute("SELECT * FROM api_tokens WHERE id = ?", (token_id,)).fetchone()
    return {"token": api_token_payload(row), "plainToken": raw_token}


@app.delete("/api/account/api-tokens/{token_id}")
def revoke_account_api_token(token_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM api_tokens WHERE id = ? AND user_id = ?", (token_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="Token 不存在")
        conn.execute("UPDATE api_tokens SET revoked_at = ? WHERE id = ?", (now_iso(), token_id))
        fresh = conn.execute("SELECT * FROM api_tokens WHERE id = ?", (token_id,)).fetchone()
    return {"token": api_token_payload(fresh)}


@app.get("/api/account/team")
def list_account_team(user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        ensure_account_defaults(conn, user)
        rows = conn.execute("SELECT * FROM team_members WHERE user_id = ? ORDER BY created_at", (user["id"],)).fetchall()
    return {"members": [team_member_payload(row) for row in rows]}


@app.post("/api/account/team")
def add_account_team_member(payload: TeamMemberCreate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    member_id = f"member_{secrets.token_hex(8)}"
    with db() as conn:
        conn.execute(
            "INSERT INTO team_members(id, user_id, email, role, status, created_at) VALUES (?, ?, ?, ?, 'invited', ?)",
            (member_id, user["id"], payload.email.strip().lower(), payload.role, now_iso()),
        )
        row = conn.execute("SELECT * FROM team_members WHERE id = ?", (member_id,)).fetchone()
    return {"member": team_member_payload(row)}


@app.delete("/api/account/team/{member_id}")
def remove_account_team_member(member_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM team_members WHERE id = ? AND user_id = ?", (member_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="成员不存在")
        if row["role"] == "owner":
            raise HTTPException(status_code=400, detail="不能移除团队拥有者")
        conn.execute("DELETE FROM team_members WHERE id = ?", (member_id,))
    return {"ok": True}


@app.get("/api/account/devices")
def list_account_devices(user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        ensure_account_defaults(conn, user)
        rows = conn.execute("SELECT * FROM account_devices WHERE user_id = ? ORDER BY last_seen_at DESC", (user["id"],)).fetchall()
    return {"devices": [device_payload(row) for row in rows]}


@app.post("/api/account/devices/{device_id}/revoke")
def revoke_account_device(device_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM account_devices WHERE id = ? AND user_id = ?", (device_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="设备不存在")
        conn.execute("UPDATE account_devices SET revoked_at = ? WHERE id = ?", (now_iso(), device_id))
        fresh = conn.execute("SELECT * FROM account_devices WHERE id = ?", (device_id,)).fetchone()
    return {"device": device_payload(fresh)}


@app.get("/api/billing/plans")
def plans() -> dict[str, Any]:
    return {
        "plans": [
            {"id": "free", "name": "Free", "quotaBytes": FREE_BYTES, "dailyLimit": 50, "price": 0},
            {"id": "pro", "name": "Pro", "quotaBytes": 50 * 1024**3, "dailyLimit": 1000, "price": 49},
            {"id": "team", "name": "Team", "quotaBytes": 500 * 1024**3, "dailyLimit": 100000, "price": 299},
        ]
    }


class BillingSubscribe(BaseModel):
    plan: str


@app.post("/api/billing/subscribe")
def subscribe_plan(payload: BillingSubscribe, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    if payload.plan not in PLAN_LIMITS:
        raise HTTPException(status_code=400, detail="套餐不存在")
    limits = PLAN_LIMITS[payload.plan]
    with db() as conn:
        conn.execute(
            "UPDATE users SET plan = ?, quota_bytes = MAX(quota_bytes, ?) WHERE id = ?",
            (payload.plan, limits["quota_bytes"], user["id"]),
        )
        fresh = conn.execute("SELECT * FROM users WHERE id = ?", (user["id"],)).fetchone()
        quota = ensure_quota_row(conn, fresh)
    return {"user": public_user(fresh), "quota": quota_payload(quota)}


@app.get("/api/ai/quota")
def ai_quota(user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        quota = ensure_quota_row(conn, user)
    return {"quota": quota_payload(quota)}


class ProjectCreate(BaseModel):
    name: str = Field(min_length=1, max_length=120)
    description: str = ""
    dataType: str = "detection"
    split: dict[str, int] = Field(default_factory=lambda: {"train": 70, "val": 20, "test": 10})


class ProjectUpdate(BaseModel):
    name: str | None = None
    description: str | None = None
    dataType: str | None = None
    split: dict[str, int] | None = None


@app.post("/api/projects")
def create_project(payload: ProjectCreate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    project_id = f"proj_{secrets.token_hex(8)}"
    ts = now_iso()
    with db() as conn:
        conn.execute(
            """
            INSERT INTO projects(id, user_id, name, description, data_type, split_json, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                project_id,
                user["id"],
                payload.name,
                payload.description,
                payload.dataType,
                json.dumps(payload.split, ensure_ascii=False),
                ts,
                ts,
            ),
        )
        row = conn.execute("SELECT * FROM projects WHERE id = ?", (project_id,)).fetchone()
    return {"project": project_payload(row)}


@app.get("/api/projects")
def list_projects(user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        rows = conn.execute("SELECT * FROM projects WHERE user_id = ? ORDER BY updated_at DESC", (user["id"],)).fetchall()
    return {"projects": [project_payload(row) for row in rows]}


@app.get("/api/projects/{project_id}")
def get_project(project_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM projects WHERE id = ? AND user_id = ?", (project_id, user["id"])).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="项目不存在")
    return {"project": project_payload(row)}


@app.put("/api/projects/{project_id}")
def update_project(project_id: str, payload: ProjectUpdate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM projects WHERE id = ? AND user_id = ?", (project_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="项目不存在")
        updated = {
            "name": payload.name if payload.name is not None else row["name"],
            "description": payload.description if payload.description is not None else row["description"],
            "data_type": payload.dataType if payload.dataType is not None else row["data_type"],
            "split_json": json.dumps(payload.split, ensure_ascii=False) if payload.split is not None else row["split_json"],
        }
        conn.execute(
            "UPDATE projects SET name = ?, description = ?, data_type = ?, split_json = ?, updated_at = ? WHERE id = ?",
            (updated["name"], updated["description"], updated["data_type"], updated["split_json"], now_iso(), project_id),
        )
        fresh = conn.execute("SELECT * FROM projects WHERE id = ?", (project_id,)).fetchone()
    return {"project": project_payload(fresh)}


@app.delete("/api/projects/{project_id}")
def delete_project(project_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM projects WHERE id = ? AND user_id = ?", (project_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="项目不存在")
        conn.execute("DELETE FROM projects WHERE id = ?", (project_id,))
    return {"ok": True}


def classify_file(filename: str, content_type: str | None = None) -> str:
    suffix = Path(filename).suffix.lower()
    if content_type and content_type.startswith("image/"):
        return "image"
    if suffix in {".jpg", ".jpeg", ".png", ".webp", ".bmp"}:
        return "image"
    if suffix in {".zip"}:
        return "export"
    return "annotation"


@app.post("/api/files/upload")
async def upload_file(
    file: UploadFile = File(...),
    project_id: str | None = Form(default=None),
    user: sqlite3.Row = Depends(current_user),
) -> dict[str, Any]:
    temp = STORAGE_ROOT / "tmp" / secrets.token_hex(16)
    temp.parent.mkdir(parents=True, exist_ok=True)
    size = 0
    with temp.open("wb") as out:
        while chunk := await file.read(1024 * 1024):
            size += len(chunk)
            if user["used_bytes"] + size > user["quota_bytes"]:
                temp.unlink(missing_ok=True)
                raise HTTPException(status_code=402, detail="存储空间不足，请清理或升级")
            out.write(chunk)

    user_dir = STORAGE_ROOT / str(user["id"]) / "files"
    user_dir.mkdir(parents=True, exist_ok=True)
    safe_name = Path(file.filename or "file").name
    final = user_dir / f"{secrets.token_hex(8)}_{safe_name}"
    shutil.move(str(temp), final)
    kind = classify_file(safe_name, file.content_type)
    with db() as conn:
        if project_id:
            project = conn.execute("SELECT * FROM projects WHERE id = ? AND user_id = ?", (project_id, user["id"])).fetchone()
            if project is None:
                raise HTTPException(status_code=404, detail="项目不存在")
        cur = conn.execute(
            """
            INSERT INTO files(user_id, project_id, name, path, size, kind, content_type, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (user["id"], project_id, safe_name, str(final), size, kind, file.content_type, now_iso()),
        )
        conn.execute("UPDATE users SET used_bytes = used_bytes + ? WHERE id = ?", (size, user["id"]))
        if project_id and kind == "image":
            conn.execute("UPDATE projects SET image_count = image_count + 1, updated_at = ? WHERE id = ?", (now_iso(), project_id))
        fresh = conn.execute("SELECT * FROM users WHERE id = ?", (user["id"],)).fetchone()
        file_row = conn.execute("SELECT * FROM files WHERE id = ?", (cur.lastrowid,)).fetchone()
    return {"file": file_payload(file_row), "user": public_user(fresh)}


@app.get("/api/files")
def list_files(
    kind: str | None = None,
    project_id: str | None = None,
    q: str | None = None,
    user: sqlite3.Row = Depends(current_user),
) -> dict[str, Any]:
    clauses = ["user_id = ?"]
    params: list[Any] = [user["id"]]
    if kind and kind != "all":
        clauses.append("kind = ?")
        params.append(kind)
    if project_id:
        clauses.append("project_id = ?")
        params.append(project_id)
    if q:
        clauses.append("name LIKE ?")
        params.append(f"%{q}%")
    with db() as conn:
        rows = conn.execute(
            f"SELECT * FROM files WHERE {' AND '.join(clauses)} ORDER BY created_at DESC",
            params,
        ).fetchall()
    return {"files": [file_payload(row) for row in rows]}


@app.get("/api/files/{file_id}/download")
def download_file(file_id: int, user: sqlite3.Row = Depends(current_user)) -> FileResponse:
    with db() as conn:
        row = conn.execute("SELECT * FROM files WHERE id = ? AND user_id = ?", (file_id, user["id"])).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="文件不存在")
    path = Path(row["path"])
    if not path.exists():
        raise HTTPException(status_code=404, detail="文件已丢失")
    return FileResponse(path, filename=row["name"], media_type=row["content_type"] or "application/octet-stream")


@app.delete("/api/files/{file_id}")
def delete_file(file_id: int, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM files WHERE id = ? AND user_id = ?", (file_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="文件不存在")
        Path(row["path"]).unlink(missing_ok=True)
        conn.execute("DELETE FROM files WHERE id = ?", (file_id,))
        conn.execute("UPDATE users SET used_bytes = MAX(0, used_bytes - ?) WHERE id = ?", (row["size"], user["id"]))
    return {"ok": True}


class TaskCreate(BaseModel):
    type: str = "auto_segment"
    title: str | None = None
    projectId: str | None = None
    fileId: int | None = None
    imageCount: int = 1


@app.post("/api/tasks")
def create_task(payload: TaskCreate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    consume_ai_quota(user, max(1, payload.imageCount))
    with db() as conn:
        task_id, _ = create_job_and_task(
            conn,
            user,
            payload.type,
            payload.title or "AI 自动处理",
            payload.imageCount,
            payload.projectId,
            payload.fileId,
            status="queued",
        )
        row = conn.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    return {"task": task_payload(row)}


@app.get("/api/tasks")
def list_tasks(status: str | None = None, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        if status and status != "all":
            rows = conn.execute(
                "SELECT * FROM tasks WHERE user_id = ? AND status = ? ORDER BY created_at DESC",
                (user["id"], status),
            ).fetchall()
        else:
            rows = conn.execute("SELECT * FROM tasks WHERE user_id = ? ORDER BY created_at DESC", (user["id"],)).fetchall()
    return {"tasks": [task_payload(row) for row in rows]}


@app.get("/api/tasks/{task_id}")
def get_task(task_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM tasks WHERE id = ? AND user_id = ?", (task_id, user["id"])).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="任务不存在")
    return {"task": task_payload(row)}


@app.post("/api/tasks/{task_id}/cancel")
def cancel_task(task_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM tasks WHERE id = ? AND user_id = ?", (task_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="任务不存在")
        conn.execute("UPDATE tasks SET status = 'cancelled', finished_at = ? WHERE id = ?", (now_iso(), task_id))
        conn.execute("UPDATE jobs SET status = 'cancelled', finished_at = ? WHERE id = ?", (now_iso(), row["job_id"]))
        fresh = conn.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    return {"task": task_payload(fresh)}


@app.post("/api/tasks/{task_id}/retry")
def retry_task(task_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    consume_ai_quota(user, 1)
    with db() as conn:
        row = conn.execute("SELECT * FROM tasks WHERE id = ? AND user_id = ?", (task_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="任务不存在")
        conn.execute(
            "UPDATE tasks SET status = 'queued', progress = 0, error_message = NULL, finished_at = NULL WHERE id = ?",
            (task_id,),
        )
        conn.execute(
            "UPDATE jobs SET status = 'queued', error = NULL, finished_at = NULL WHERE id = ?",
            (row["job_id"],),
        )
        fresh = conn.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    return {"task": task_payload(fresh)}


class DatasetExportCreate(BaseModel):
    projectId: str | None = None
    train: int = 70
    val: int = 20
    test: int = 10
    format: str = "yolo"
    includeImages: bool = True
    includeLabels: bool = True
    includeConfig: bool = True
    naming: str = "original"


def write_dataset_zip(path: Path, files: list[sqlite3.Row], config: DatasetExportCreate) -> None:
    splits = ["train", "val", "test"]
    weights = [config.train, config.val, config.test]
    total_weight = max(1, sum(weights))
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zf:
        for index, file_row in enumerate(files):
            cursor = (index * total_weight) // max(1, len(files))
            split = "train"
            if cursor >= weights[0] + weights[1]:
                split = "test"
            elif cursor >= weights[0]:
                split = "val"
            source = Path(file_row["path"])
            if config.includeImages and source.exists() and file_row["kind"] == "image":
                zf.write(source, f"dataset/images/{split}/{file_row['name']}")
            if config.includeLabels:
                zf.writestr(f"dataset/labels/{split}/{Path(file_row['name']).stem}.txt", "")
        if config.includeConfig:
            zf.writestr(
                "dataset/dataset.yaml",
                "path: ./dataset\ntrain: images/train\nval: images/val\ntest: images/test\nnames: []\n",
            )
            zf.writestr("dataset/classes.txt", "")
            zf.writestr("dataset/README.md", "# MaskFlow Dataset\n\nGenerated by MaskFlow API.\n")


@app.post("/api/export/dataset")
def create_dataset_export(payload: DatasetExportCreate, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    consume_ai_quota(user, 1)
    export_id = f"export_{secrets.token_hex(10)}"
    export_dir = STORAGE_ROOT / str(user["id"]) / "exports"
    export_dir.mkdir(parents=True, exist_ok=True)
    zip_path = export_dir / f"{export_id}.zip"
    with db() as conn:
        clauses = ["user_id = ?", "kind = 'image'"]
        params: list[Any] = [user["id"]]
        if payload.projectId:
            clauses.append("project_id = ?")
            params.append(payload.projectId)
        files = conn.execute(f"SELECT * FROM files WHERE {' AND '.join(clauses)} ORDER BY created_at", params).fetchall()
        task_id, _ = create_job_and_task(
            conn,
            user,
            "dataset_export",
            "数据集导出",
            max(1, len(files)),
            payload.projectId,
            job_type="dataset.export",
        )
        conn.execute(
            """
            INSERT INTO dataset_exports(id, user_id, project_id, task_id, status, config_json, created_at)
            VALUES (?, ?, ?, ?, 'running', ?, ?)
            """,
            (export_id, user["id"], payload.projectId, task_id, model_json(payload), now_iso()),
        )
    try:
        write_dataset_zip(zip_path, files, payload)
        size = zip_path.stat().st_size
        with db() as conn:
            conn.execute(
                "UPDATE dataset_exports SET status = 'completed', path = ?, size = ?, finished_at = ? WHERE id = ?",
                (str(zip_path), size, now_iso(), export_id),
            )
    except Exception as exc:
        with db() as conn:
            row = conn.execute("SELECT job_id FROM tasks WHERE id = ?", (task_id,)).fetchone()
        finish_task(task_id, row["job_id"], error=str(exc))
        raise
    with db() as conn:
        task = conn.execute("SELECT job_id FROM tasks WHERE id = ?", (task_id,)).fetchone()
    finish_task(task_id, task["job_id"], {"exportId": export_id})
    with db() as conn:
        row = conn.execute("SELECT * FROM dataset_exports WHERE id = ?", (export_id,)).fetchone()
    return {"export": export_payload(row)}


@app.get("/api/export/{export_id}")
def get_export(export_id: str, user: sqlite3.Row = Depends(current_user)) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM dataset_exports WHERE id = ? AND user_id = ?", (export_id, user["id"])).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="导出不存在")
    return {"export": export_payload(row)}


@app.get("/api/export/{export_id}/download")
def download_export(export_id: str, user: sqlite3.Row = Depends(current_user)) -> FileResponse:
    with db() as conn:
        row = conn.execute("SELECT * FROM dataset_exports WHERE id = ? AND user_id = ?", (export_id, user["id"])).fetchone()
    if row is None or not row["path"]:
        raise HTTPException(status_code=404, detail="导出不存在")
    path = Path(row["path"])
    if not path.exists():
        raise HTTPException(status_code=404, detail="导出文件已丢失")
    return FileResponse(path, filename=f"{export_id}.zip", media_type="application/zip")


async def forward_upload(endpoint: str, image: UploadFile, fields: dict[str, str]) -> dict[str, Any]:
    data = await image.read()
    files = {"image": (image.filename or "image.jpg", data, image.content_type or "application/octet-stream")}
    async with httpx.AsyncClient(timeout=1800) as client:
        response = await client.post(f"{SAM_SERVICE_URL}{endpoint}", files=files, data=fields)
    if response.status_code >= 400:
        raise HTTPException(status_code=response.status_code, detail=response.text)
    return response.json()


@app.post("/api/segment")
async def segment_proxy(
    image: UploadFile = File(...),
    prompt: str = Form(""),
    conf: float = Form(0.25),
    half: bool = Form(True),
    user: sqlite3.Row | None = Depends(optional_user),
) -> dict[str, Any]:
    task_id = job_id = None
    if user is not None:
        consume_ai_quota(user, 1)
        with db() as conn:
            task_id, job_id = create_job_and_task(
                conn,
                user,
                "auto_segment",
                f"自动分割_{Path(image.filename or 'image').name}",
                1,
                job_type="sam.segment",
                params={"prompt": prompt, "conf": conf, "half": half},
            )
    try:
        result = await forward_upload(
            "/api/segment",
            image,
            {"prompt": prompt, "conf": str(conf), "half": "true" if half else "false"},
        )
        if task_id and job_id:
            finish_task(task_id, job_id, {"summary": result.get("summary")})
            result["taskId"] = task_id
            result["jobId"] = job_id
        return result
    except Exception as exc:
        if task_id and job_id:
            finish_task(task_id, job_id, error=str(exc))
        raise


@app.post("/api/annotation/masks")
async def masks_proxy(
    image: UploadFile = File(...),
    conf: float = Form(0.25),
    user: sqlite3.Row | None = Depends(optional_user),
) -> dict[str, Any]:
    task_id = job_id = None
    if user is not None:
        consume_ai_quota(user, 1)
        with db() as conn:
            task_id, job_id = create_job_and_task(
                conn,
                user,
                "auto_masks",
                f"自动标注_{Path(image.filename or 'image').name}",
                1,
                job_type="sam.masks",
                params={"conf": conf},
            )
    try:
        result = await forward_upload("/api/annotation/masks", image, {"conf": str(conf)})
        if task_id and job_id:
            finish_task(task_id, job_id, {"maskCount": len(result.get("masks", []))})
            result["taskId"] = task_id
            result["jobId"] = job_id
        return result
    except Exception as exc:
        if task_id and job_id:
            finish_task(task_id, job_id, error=str(exc))
        raise


class JobCreate(BaseModel):
    type: str
    userId: int
    projectId: str | None = None
    input: dict[str, Any] = Field(default_factory=dict)
    params: dict[str, Any] = Field(default_factory=dict)


@app.post("/v1/jobs")
def compute_create_job(payload: JobCreate) -> dict[str, Any]:
    job_id = f"job_{secrets.token_hex(10)}"
    with db() as conn:
        conn.execute(
            """
            INSERT INTO jobs(id, app, type, user_id, project_id, pool, priority, status, resources_json, input_json,
                             params_json, reserved_credits, created_at)
            VALUES (?, 'maskflow', ?, ?, ?, 'platform', 'free', 'queued', ?, ?, ?, 10, ?)
            """,
            (
                job_id,
                payload.type,
                payload.userId,
                payload.projectId,
                json.dumps({"gpu": 1, "timeoutSec": 600}, ensure_ascii=False),
                json.dumps(payload.input, ensure_ascii=False),
                json.dumps(payload.params, ensure_ascii=False),
                now_iso(),
            ),
        )
        row = conn.execute("SELECT * FROM jobs WHERE id = ?", (job_id,)).fetchone()
    return {"job": compute_job_payload(row)}


@app.get("/v1/jobs")
def compute_list_jobs(status: str | None = None) -> dict[str, Any]:
    with db() as conn:
        if status:
            rows = conn.execute("SELECT * FROM jobs WHERE status = ? ORDER BY created_at DESC", (status,)).fetchall()
        else:
            rows = conn.execute("SELECT * FROM jobs ORDER BY created_at DESC").fetchall()
    return {"jobs": [compute_job_payload(row) for row in rows]}


@app.get("/v1/jobs/{job_id}")
def compute_get_job(job_id: str) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM jobs WHERE id = ?", (job_id,)).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="Job not found")
    return {"job": compute_job_payload(row)}


@app.post("/v1/jobs/{job_id}/cancel")
def compute_cancel_job(job_id: str) -> dict[str, Any]:
    with db() as conn:
        conn.execute("UPDATE jobs SET status = 'cancelled', finished_at = ? WHERE id = ?", (now_iso(), job_id))
        row = conn.execute("SELECT * FROM jobs WHERE id = ?", (job_id,)).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="Job not found")
    return {"job": compute_job_payload(row)}


@app.post("/v1/jobs/{job_id}/retry")
def compute_retry_job(job_id: str) -> dict[str, Any]:
    with db() as conn:
        conn.execute("UPDATE jobs SET status = 'queued', error = NULL, finished_at = NULL WHERE id = ?", (job_id,))
        row = conn.execute("SELECT * FROM jobs WHERE id = ?", (job_id,)).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="Job not found")
    return {"job": compute_job_payload(row)}


class JobEventCreate(BaseModel):
    eventType: str
    payload: dict[str, Any] = Field(default_factory=dict)
    status: str | None = None
    progress: float | None = None
    error: str | None = None


@app.post("/v1/jobs/{job_id}/events")
def compute_job_event(job_id: str, payload: JobEventCreate) -> dict[str, Any]:
    ts = now_iso()
    with db() as conn:
        job = conn.execute("SELECT * FROM jobs WHERE id = ?", (job_id,)).fetchone()
        if job is None:
            raise HTTPException(status_code=404, detail="Job not found")
        conn.execute(
            "INSERT INTO job_events(job_id, event_type, payload_json, created_at) VALUES (?, ?, ?, ?)",
            (job_id, payload.eventType, json.dumps(payload.payload, ensure_ascii=False), ts),
        )
        if payload.status:
            finished = ts if payload.status in {"succeeded", "failed", "cancelled"} else None
            conn.execute(
                "UPDATE jobs SET status = ?, error = COALESCE(?, error), finished_at = COALESCE(?, finished_at) WHERE id = ?",
                (payload.status, payload.error, finished, job_id),
            )
            task_status = {"succeeded": "completed", "failed": "failed", "cancelled": "cancelled"}.get(payload.status, "running")
            progress = payload.progress if payload.progress is not None else (1 if payload.status == "succeeded" else 0.5)
            conn.execute(
                "UPDATE tasks SET status = ?, progress = ?, error_message = COALESCE(?, error_message), finished_at = COALESCE(?, finished_at) WHERE job_id = ?",
                (task_status, progress, payload.error, finished, job_id),
            )
        event = conn.execute("SELECT * FROM job_events WHERE rowid = last_insert_rowid()").fetchone()
        fresh = conn.execute("SELECT * FROM jobs WHERE id = ?", (job_id,)).fetchone()
    return {"event": dict(event), "job": compute_job_payload(fresh)}


class NodeRegister(BaseModel):
    ownerId: int = 0
    pool: str = "platform-gpu"
    gpuModel: str | None = None
    vramGb: int | None = None
    region: str | None = None
    pricePerHour: float | None = None


class NodeHeartbeat(BaseModel):
    status: str = "online"
    gpuModel: str | None = None
    vramGb: int | None = None
    region: str | None = None


def node_by_key(conn: sqlite3.Connection, node_id: str, api_key: str | None) -> sqlite3.Row:
    node = conn.execute("SELECT * FROM nodes WHERE id = ?", (node_id,)).fetchone()
    if node is None:
        raise HTTPException(status_code=404, detail="Node not found")
    if api_key and not hmac.compare_digest(api_key, node["api_key"]):
        raise HTTPException(status_code=401, detail="Invalid node api key")
    return node


@app.post("/v1/nodes/register")
def register_node(payload: NodeRegister) -> dict[str, Any]:
    node_id = f"node_{secrets.token_hex(8)}"
    api_key = secrets.token_urlsafe(32)
    ts = now_iso()
    with db() as conn:
        conn.execute(
            """
            INSERT INTO nodes(id, owner_id, pool, status, gpu_model, vram_gb, region, price_per_hour, api_key, created_at, last_heartbeat)
            VALUES (?, ?, ?, 'pending', ?, ?, ?, ?, ?, ?, ?)
            """,
            (node_id, payload.ownerId, payload.pool, payload.gpuModel, payload.vramGb, payload.region, payload.pricePerHour, api_key, ts, ts),
        )
        node = conn.execute("SELECT * FROM nodes WHERE id = ?", (node_id,)).fetchone()
    data = node_payload(node)
    data["apiKey"] = api_key
    return {"node": data}


@app.post("/v1/nodes/{node_id}/heartbeat")
def heartbeat_node(
    node_id: str,
    payload: NodeHeartbeat,
    x_node_api_key: str | None = Header(default=None, alias="X-Node-Api-Key"),
) -> dict[str, Any]:
    with db() as conn:
        node_by_key(conn, node_id, x_node_api_key)
        conn.execute(
            """
            UPDATE nodes SET status = ?, gpu_model = COALESCE(?, gpu_model), vram_gb = COALESCE(?, vram_gb),
                             region = COALESCE(?, region), last_heartbeat = ?
            WHERE id = ?
            """,
            (payload.status, payload.gpuModel, payload.vramGb, payload.region, now_iso(), node_id),
        )
        node = conn.execute("SELECT * FROM nodes WHERE id = ?", (node_id,)).fetchone()
    return {"node": node_payload(node)}


@app.get("/v1/nodes")
def list_nodes(status: str | None = None, pool: str | None = None) -> dict[str, Any]:
    clauses: list[str] = []
    params: list[Any] = []
    if status:
        clauses.append("status = ?")
        params.append(status)
    if pool:
        clauses.append("pool = ?")
        params.append(pool)
    sql = "SELECT * FROM nodes"
    if clauses:
        sql += " WHERE " + " AND ".join(clauses)
    sql += " ORDER BY created_at DESC"
    with db() as conn:
        rows = conn.execute(sql, params).fetchall()
    return {"nodes": [node_payload(row) for row in rows]}


@app.get("/v1/nodes/{node_id}")
def get_node(node_id: str) -> dict[str, Any]:
    with db() as conn:
        node = conn.execute("SELECT * FROM nodes WHERE id = ?", (node_id,)).fetchone()
    if node is None:
        raise HTTPException(status_code=404, detail="Node not found")
    return {"node": node_payload(node)}


@app.post("/v1/nodes/{node_id}/offline")
def offline_node(node_id: str) -> dict[str, Any]:
    with db() as conn:
        conn.execute("UPDATE nodes SET status = 'offline', last_heartbeat = ? WHERE id = ?", (now_iso(), node_id))
        node = conn.execute("SELECT * FROM nodes WHERE id = ?", (node_id,)).fetchone()
    if node is None:
        raise HTTPException(status_code=404, detail="Node not found")
    return {"node": node_payload(node)}


@app.post("/v1/nodes/{node_id}/approve")
def approve_node(node_id: str) -> dict[str, Any]:
    with db() as conn:
        conn.execute("UPDATE nodes SET status = 'online', approved_at = ?, last_heartbeat = ? WHERE id = ?", (now_iso(), now_iso(), node_id))
        node = conn.execute("SELECT * FROM nodes WHERE id = ?", (node_id,)).fetchone()
    if node is None:
        raise HTTPException(status_code=404, detail="Node not found")
    return {"node": node_payload(node)}


@app.get("/v1/nodes/{node_id}/jobs/poll")
def poll_node_job(
    node_id: str,
    x_node_api_key: str | None = Header(default=None, alias="X-Node-Api-Key"),
) -> dict[str, Any]:
    ts = now_iso()
    with db() as conn:
        node = node_by_key(conn, node_id, x_node_api_key)
        if node["status"] not in {"online", "busy"}:
            return {"job": None}
        job = conn.execute(
            "SELECT * FROM jobs WHERE status = 'queued' ORDER BY created_at ASC LIMIT 1"
        ).fetchone()
        if job is None:
            conn.execute("UPDATE nodes SET status = 'online', last_heartbeat = ? WHERE id = ?", (ts, node_id))
            return {"job": None}
        conn.execute("UPDATE jobs SET status = 'running', node_id = ?, started_at = COALESCE(started_at, ?) WHERE id = ?", (node_id, ts, job["id"]))
        conn.execute("UPDATE tasks SET status = 'running', progress = 0.2, started_at = COALESCE(started_at, ?) WHERE job_id = ?", (ts, job["id"]))
        conn.execute("UPDATE nodes SET status = 'busy', last_heartbeat = ? WHERE id = ?", (ts, node_id))
        conn.execute(
            "INSERT OR REPLACE INTO allocations(job_id, node_id, gpu_index, started_at) VALUES (?, ?, 0, ?)",
            (job["id"], node_id, ts),
        )
        fresh = conn.execute("SELECT * FROM jobs WHERE id = ?", (job["id"],)).fetchone()
    return {"job": compute_job_payload(fresh)}


class PoolCreate(BaseModel):
    id: str | None = None
    name: str
    type: str = "platform-gpu"
    region: str | None = None
    capacity: dict[str, Any] = Field(default_factory=dict)
    policy: dict[str, Any] = Field(default_factory=dict)


class PoolUpdate(BaseModel):
    name: str | None = None
    type: str | None = None
    region: str | None = None
    status: str | None = None
    capacity: dict[str, Any] | None = None
    policy: dict[str, Any] | None = None


@app.get("/v1/pools")
def list_pools() -> dict[str, Any]:
    with db() as conn:
        rows = conn.execute("SELECT * FROM pools ORDER BY created_at DESC").fetchall()
    return {"pools": [pool_payload(row) for row in rows]}


@app.post("/v1/pools")
def create_pool(payload: PoolCreate) -> dict[str, Any]:
    pool_id = payload.id or f"pool_{secrets.token_hex(6)}"
    ts = now_iso()
    with db() as conn:
        conn.execute(
            """
            INSERT INTO pools(id, name, type, region, capacity_json, policy_json, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                pool_id,
                payload.name,
                payload.type,
                payload.region,
                json.dumps(payload.capacity, ensure_ascii=False),
                json.dumps(payload.policy, ensure_ascii=False),
                ts,
                ts,
            ),
        )
        row = conn.execute("SELECT * FROM pools WHERE id = ?", (pool_id,)).fetchone()
    return {"pool": pool_payload(row)}


@app.put("/v1/pools/{pool_id}")
def update_pool(pool_id: str, payload: PoolUpdate) -> dict[str, Any]:
    with db() as conn:
        row = conn.execute("SELECT * FROM pools WHERE id = ?", (pool_id,)).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="Pool not found")
        conn.execute(
            """
            UPDATE pools SET name = ?, type = ?, region = ?, status = ?, capacity_json = ?, policy_json = ?, updated_at = ?
            WHERE id = ?
            """,
            (
                payload.name if payload.name is not None else row["name"],
                payload.type if payload.type is not None else row["type"],
                payload.region if payload.region is not None else row["region"],
                payload.status if payload.status is not None else row["status"],
                json.dumps(payload.capacity, ensure_ascii=False) if payload.capacity is not None else row["capacity_json"],
                json.dumps(payload.policy, ensure_ascii=False) if payload.policy is not None else row["policy_json"],
                now_iso(),
                pool_id,
            ),
        )
        fresh = conn.execute("SELECT * FROM pools WHERE id = ?", (pool_id,)).fetchone()
    return {"pool": pool_payload(fresh)}


class PricingCreate(BaseModel):
    name: str
    resourceType: str = "gpu"
    pool: str | None = None
    region: str | None = None
    unitPrice: float
    billingUnit: str = "hour"
    status: str = "active"


@app.get("/v1/pricing")
def list_pricing() -> dict[str, Any]:
    with db() as conn:
        rows = conn.execute("SELECT * FROM pricing_rules ORDER BY updated_at DESC").fetchall()
    return {"rules": [pricing_payload(row) for row in rows]}


@app.post("/v1/pricing/rules")
def create_pricing_rule(payload: PricingCreate) -> dict[str, Any]:
    rule_id = f"price_{secrets.token_hex(8)}"
    ts = now_iso()
    with db() as conn:
        conn.execute(
            """
            INSERT INTO pricing_rules(id, name, resource_type, pool, region, unit_price, billing_unit, status, effective_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (rule_id, payload.name, payload.resourceType, payload.pool, payload.region, payload.unitPrice, payload.billingUnit, payload.status, ts, ts),
        )
        row = conn.execute("SELECT * FROM pricing_rules WHERE id = ?", (rule_id,)).fetchone()
    return {"rule": pricing_payload(row)}


@app.get("/v1/wallet/balance")
def wallet_balance(user_id: int = 0) -> dict[str, Any]:
    with db() as conn:
        total = conn.execute("SELECT COALESCE(SUM(delta), 0) AS total FROM wallet_ledger WHERE user_id = ?", (user_id,)).fetchone()["total"]
        rows = conn.execute("SELECT * FROM wallet_ledger WHERE user_id = ? ORDER BY created_at DESC LIMIT 50", (user_id,)).fetchall()
    return {"userId": user_id, "balance": 1000 + total, "ledger": [dict(row) for row in rows]}


@app.get("/v1/settlements")
def list_settlements(status: str | None = None) -> dict[str, Any]:
    with db() as conn:
        if status:
            rows = conn.execute("SELECT * FROM settlements WHERE status = ? ORDER BY created_at DESC", (status,)).fetchall()
        else:
            rows = conn.execute("SELECT * FROM settlements ORDER BY created_at DESC").fetchall()
    return {"settlements": [settlement_payload(row) for row in rows]}


class SettlementCreate(BaseModel):
    providerId: int
    period: str
    nodeCount: int = 0
    grossAmount: float = 0
    platformFee: float = 0
    status: str = "pending"


@app.post("/v1/settlements")
def create_settlement(payload: SettlementCreate) -> dict[str, Any]:
    settlement_id = f"settle_{secrets.token_hex(8)}"
    net = payload.grossAmount - payload.platformFee
    with db() as conn:
        conn.execute(
            """
            INSERT INTO settlements(id, provider_id, period, node_count, gross_amount, platform_fee, net_amount, status, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (settlement_id, payload.providerId, payload.period, payload.nodeCount, payload.grossAmount, payload.platformFee, net, payload.status, now_iso()),
        )
        row = conn.execute("SELECT * FROM settlements WHERE id = ?", (settlement_id,)).fetchone()
    return {"settlement": settlement_payload(row)}
