# MaskFlow 源码目录

这里统一存放 MaskFlow 的所有代码。文档、模型、原型图等非代码资源不放在这里，分别在项目根目录的 `文档` 和 `资源` 中维护。

## 目录职责

```text
src/
  frontend/   前端应用代码
  backend/    业务后端代码
  ai/         AI 推理服务代码
  legacy/     旧版代码备份，仅用于参考
```

## 当前项目

```text
frontend/maskflow-web/
```

Vue 3 + Vite 前端。负责首页、登录注册、控制台、上传图片、SAM 分割、YOLO 标注、数据集导出、处理记录、账单套餐和账户设置等页面。

```text
backend/maskflow-api/
```

ASP.NET Core WebAPI 后端。当前采用 Controller 组织接口，并按 `Application`、`Controllers`、`Infrastructure` 做基础分层。后续替换数据库、增加复杂业务逻辑、引入队列或权限系统时，优先在这里扩展。

```text
ai/sam-inference/
```

Python SAM 推理服务。负责图片分割、类别 overlay 生成、YOLO mask 辅助标注等 AI 能力。模型文件不放在源码目录，模型位于 `../资源/模型/sam3.pt`。

```text
legacy/
```

旧版静态前端和旧版 Python API。只作为迁移参考，不参与当前主流程开发。

## 本地启动

### 启动后端

需先配置 MySQL 连接（环境变量 `MASKFLOW_MYSQL`）。Docker 部署时由 `docker-compose.yml` 自动注入；本地开发可先执行 `docker compose up -d mysql`。

```powershell
$env:MASKFLOW_MYSQL = "Server=127.0.0.1;Port=3306;Database=maskflow;User ID=maskflow;Password=maskflow;Allow User Variables=true;"
dotnet run --project src\backend\maskflow-api\MaskFlow.Api.csproj --urls http://127.0.0.1:8010
```

业务状态仅持久化到 MySQL，不再使用 `maskflow-state.json`。

Swagger 调试界面：

```text
http://127.0.0.1:8010/swagger
```

### 启动前端

```powershell
cd src\frontend\maskflow-web
npm install
npm run dev -- --host 127.0.0.1 --port 3010
```

访问：

```text
http://127.0.0.1:3010
```

### 启动 SAM 推理服务

```powershell
cd src\ai\sam-inference
python -m uvicorn app.main:app --host 127.0.0.1 --port 8001
```

如果使用 Docker Compose，直接在项目根目录执行：

```powershell
docker compose up -d --build
```

## 开发约定

- 新功能代码优先放在 `frontend`、`backend`、`ai` 对应目录中。
- 不要把模型、原型图、设计文档放进 `src`。
- 不要提交 `bin`、`obj`、`dist`、`node_modules`、`.vs`、`__pycache__` 等生成物。
- 后端新增接口时优先使用 Controller，并在 Swagger 中设置清晰分类。
- 前端调用后端时尽量保持 `/api/...` 路径稳定，避免页面和后端同时大面积改动。
