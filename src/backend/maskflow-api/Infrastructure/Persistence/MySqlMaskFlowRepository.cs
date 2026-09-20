using System.Data;
using System.Text.Json;
using MySqlConnector;

public sealed class MySqlMaskFlowRepository : IMaskFlowRepository
{
    readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    readonly string connectionString;

    public MySqlMaskFlowRepository()
    {
        var configured = Environment.GetEnvironmentVariable("MASKFLOW_MYSQL");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "MASKFLOW_MYSQL environment variable is required. Example: Server=127.0.0.1;Port=3306;Database=maskflow;User ID=maskflow;Password=maskflow;Allow User Variables=true;");
        }

        connectionString = configured;
    }

    public async Task EnsureSchemaAsync()
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var database = string.IsNullOrWhiteSpace(builder.Database) ? "maskflow" : builder.Database;
        builder.Database = "";

        await using (var serverConnection = new MySqlConnection(builder.ConnectionString))
        {
            await serverConnection.OpenAsync();
            await ExecAsync(serverConnection, null, $"CREATE DATABASE IF NOT EXISTS `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
        }

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var sql in SchemaSql)
        {
            try { await ExecAsync(connection, null, sql); }
            catch (MySqlException ex) when (ex.Message.Contains("Duplicate")) { }
        }
    }

    public async Task<MaskFlowState?> LoadAsync()
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        if (!await HasAnyDataAsync(connection)) return await TryLoadLegacyStateAsync(connection);

        var state = new MaskFlowState();
        await LoadCountersAsync(connection, state);
        await LoadUsersAsync(connection, state);
        await LoadSessionsAsync(connection, state);
        await LoadProjectsAsync(connection, state);
        await LoadProjectLabelsAsync(connection, state);
        await LoadFilesAsync(connection, state);
        await LoadAnnotationSetsAsync(connection, state);
        await LoadTasksAsync(connection, state);
        await LoadJobsAsync(connection, state);
        await LoadJobEventsAsync(connection, state);
        await LoadNodesAsync(connection, state);
        await LoadPoolsAsync(connection, state);
        await LoadPricingRulesAsync(connection, state);
        await LoadWalletLedgerAsync(connection, state);
        await LoadSettlementsAsync(connection, state);
        await LoadApiTokensAsync(connection, state);
        await LoadTeamMembersAsync(connection, state);
        await LoadDevicesAsync(connection, state);
        await LoadExportsAsync(connection, state);
        await LoadQuotasAsync(connection, state);
        await LoadNotificationsAsync(connection, state);
        return state;
    }

    public async Task SaveAsync(MaskFlowState state, IReadOnlyCollection<string>? syncProjectLabelIds = null)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        try
        {
            await UpsertAsync(connection, tx, "app_counters",
                ["id", "next_user_id", "next_file_id", "next_event_id"],
                [1, state.NextUserId, state.NextFileId, state.NextEventId]);

            await PruneExceptAsync(connection, tx, "users", "id", state.Users.Select(x => x.Id));
            foreach (var x in state.Users)
                await UpsertAsync(connection, tx, "users",
                    ["id", "email", "password_hash", "salt", "plan", "quota_bytes", "used_bytes", "created_at", "username", "phone", "avatar_path"],
                    [x.Id, x.Email, x.PasswordHash, x.Salt, x.Plan, x.QuotaBytes, x.UsedBytes, x.CreatedAt, x.Username, x.Phone, x.AvatarPath]);

            await PruneExceptAsync(connection, tx, "sessions", "token", state.Sessions.Select(x => x.Token));
            foreach (var x in state.Sessions)
                await UpsertAsync(connection, tx, "sessions", ["token", "user_id", "created_at", "expires_at", "device_id"], [x.Token, x.UserId, x.CreatedAt, x.ExpiresAt, x.DeviceId]);

            await PruneExceptAsync(connection, tx, "projects", "id", state.Projects.Select(x => x.Id));
            foreach (var x in state.Projects)
                await UpsertAsync(connection, tx, "projects",
                    ["id", "user_id", "name", "description", "data_type", "split_json", "image_count", "annotation_count", "created_at", "updated_at"],
                    [x.Id, x.UserId, x.Name, x.Description, x.DataType, Json(x.Split), x.ImageCount, x.AnnotationCount, x.CreatedAt, x.UpdatedAt]);

            if (syncProjectLabelIds is { Count: > 0 })
            {
                await SyncProjectLabelsAsync(connection, tx, state, syncProjectLabelIds);
            }

            await PruneExceptAsync(connection, tx, "files", "id", state.Files.Select(x => x.Id));
            foreach (var x in state.Files)
                await UpsertAsync(connection, tx, "files",
                    ["id", "user_id", "project_id", "name", "object_path", "size_bytes", "kind", "content_type", "created_at", "download_url"],
                    [x.Id, x.UserId, x.ProjectId, x.Name, x.Path, x.Size, x.Kind, x.ContentType, x.CreatedAt, x.DownloadUrl]);

            await PruneExceptAsync(connection, tx, "annotation_sets", "id", state.AnnotationSets.Select(x => x.Id));
            foreach (var x in state.AnnotationSets)
                await UpsertAsync(connection, tx, "annotation_sets",
                    ["id", "user_id", "file_id", "width", "height", "annotations_json", "yolo_txt", "created_at", "updated_at"],
                    [x.Id, x.UserId, x.FileId, x.Width, x.Height, Json(x.Annotations), x.YoloTxt, x.CreatedAt, x.UpdatedAt]);

            await PruneExceptAsync(connection, tx, "tasks", "id", state.Tasks.Select(x => x.Id));
            foreach (var x in state.Tasks)
                await UpsertAsync(connection, tx, "tasks",
                    ["id", "user_id", "job_id", "type", "title", "project_id", "file_id", "image_count", "status", "progress", "result_json", "error_message", "created_at", "started_at", "finished_at"],
                    [x.Id, x.UserId, x.JobId, x.Type, x.Title, x.ProjectId, x.FileId, x.ImageCount, x.Status, x.Progress, JsonOrNull(x.Result), x.ErrorMessage, x.CreatedAt, x.StartedAt, x.FinishedAt]);

            await PruneExceptAsync(connection, tx, "jobs", "id", state.Jobs.Select(x => x.Id));
            foreach (var x in state.Jobs)
                await UpsertAsync(connection, tx, "jobs",
                    ["id", "app", "type", "user_id", "project_id", "pool", "priority", "status", "resources_json", "input_json", "output_json", "params_json", "node_id", "reserved_credits", "charged_credits", "error", "created_at", "started_at", "finished_at"],
                    [x.Id, x.App, x.Type, x.UserId, x.ProjectId, x.Pool, x.Priority, x.Status, Json(x.Resources), Json(x.Input), JsonOrNull(x.Output), Json(x.Params), x.NodeId, x.ReservedCredits, x.ChargedCredits, x.Error, x.CreatedAt, x.StartedAt, x.FinishedAt]);

            await PruneExceptAsync(connection, tx, "job_events", "id", state.JobEvents.Select(x => x.Id));
            foreach (var x in state.JobEvents)
                await UpsertAsync(connection, tx, "job_events", ["id", "job_id", "event_type", "payload_json", "created_at"], [x.Id, x.JobId, x.EventType, Json(x.Payload), x.CreatedAt]);

            await PruneExceptAsync(connection, tx, "nodes", "id", state.Nodes.Select(x => x.Id));
            foreach (var x in state.Nodes)
                await UpsertAsync(connection, tx, "nodes",
                    ["id", "owner_id", "pool", "status", "gpu_model", "vram_gb", "region", "price_per_hour", "reputation", "api_key", "created_at", "approved_at", "last_heartbeat"],
                    [x.Id, x.OwnerId, x.Pool, x.Status, x.GpuModel, x.VramGb, x.Region, x.PricePerHour, x.Reputation, x.ApiKey, x.CreatedAt, x.ApprovedAt, x.LastHeartbeat]);

            await PruneExceptAsync(connection, tx, "pools", "id", state.Pools.Select(x => x.Id));
            foreach (var x in state.Pools)
                await UpsertAsync(connection, tx, "pools",
                    ["id", "name", "type", "region", "status", "capacity_json", "policy_json", "created_at", "updated_at"],
                    [x.Id, x.Name, x.Type, x.Region, x.Status, Json(x.Capacity), Json(x.Policy), x.CreatedAt, x.UpdatedAt]);

            await PruneExceptAsync(connection, tx, "pricing_rules", "id", state.PricingRules.Select(x => x.Id));
            foreach (var x in state.PricingRules)
                await UpsertAsync(connection, tx, "pricing_rules",
                    ["id", "name", "resource_type", "pool", "region", "unit_price", "billing_unit", "status", "effective_at", "updated_at"],
                    [x.Id, x.Name, x.ResourceType, x.Pool, x.Region, x.UnitPrice, x.BillingUnit, x.Status, x.EffectiveAt, x.UpdatedAt]);

            await PruneExceptAsync(connection, tx, "wallet_ledger", "id", state.WalletLedger.Select(x => x.Id));
            foreach (var x in state.WalletLedger)
                await UpsertAsync(connection, tx, "wallet_ledger", ["id", "user_id", "delta", "reason", "job_id", "created_at"], [x.Id, x.UserId, x.Delta, x.Reason, x.JobId, x.CreatedAt]);

            await PruneExceptAsync(connection, tx, "settlements", "id", state.Settlements.Select(x => x.Id));
            foreach (var x in state.Settlements)
                await UpsertAsync(connection, tx, "settlements",
                    ["id", "provider_id", "period", "node_count", "gross_amount", "platform_fee", "net_amount", "status", "created_at", "paid_at"],
                    [x.Id, x.ProviderId, x.Period, x.NodeCount, x.GrossAmount, x.PlatformFee, x.NetAmount, x.Status, x.CreatedAt, x.PaidAt]);

            await PruneExceptAsync(connection, tx, "api_tokens", "id", state.ApiTokens.Select(x => x.Id));
            foreach (var x in state.ApiTokens)
                await UpsertAsync(connection, tx, "api_tokens",
                    ["id", "user_id", "name", "token_hash", "token_prefix", "created_at", "last_used_at", "revoked_at"],
                    [x.Id, x.UserId, x.Name, x.TokenHash, x.TokenPrefix, x.CreatedAt, x.LastUsedAt, x.RevokedAt]);

            await PruneExceptAsync(connection, tx, "team_members", "id", state.TeamMembers.Select(x => x.Id));
            foreach (var x in state.TeamMembers)
                await UpsertAsync(connection, tx, "team_members", ["id", "user_id", "email", "role", "status", "created_at"], [x.Id, x.UserId, x.Email, x.Role, x.Status, x.CreatedAt]);

            await PruneExceptAsync(connection, tx, "account_devices", "id", state.Devices.Select(x => x.Id));
            foreach (var x in state.Devices)
                await UpsertAsync(connection, tx, "account_devices",
                    ["id", "user_id", "name", "ip", "user_agent", "created_at", "last_seen_at", "revoked_at"],
                    [x.Id, x.UserId, x.Name, x.Ip, x.UserAgent, x.CreatedAt, x.LastSeenAt, x.RevokedAt]);

            await PruneExceptAsync(connection, tx, "dataset_exports", "id", state.Exports.Select(x => x.Id));
            foreach (var x in state.Exports)
                await UpsertAsync(connection, tx, "dataset_exports",
                    ["id", "user_id", "project_id", "task_id", "status", "object_path", "size_bytes", "config_json", "created_at", "finished_at", "error_message", "download_url"],
                    [x.Id, x.UserId, x.ProjectId, x.TaskId, x.Status, x.Path, x.Size, Json(x.Config), x.CreatedAt, x.FinishedAt, x.ErrorMessage, x.DownloadUrl]);

            await PruneExceptAsync(connection, tx, "ai_quotas", "user_id", state.Quotas.Keys.Select(int.Parse));
            foreach (var pair in state.Quotas)
                await UpsertAsync(connection, tx, "ai_quotas", ["user_id", "plan", "daily_limit", "daily_used", "daily_reset_at"], [int.Parse(pair.Key), pair.Value.Plan, pair.Value.DailyLimit, pair.Value.DailyUsed, pair.Value.DailyResetAt.ToDateTime(TimeOnly.MinValue)]);

            await PruneExceptAsync(connection, tx, "notification_settings", "user_id", state.NotificationSettings.Keys.Select(int.Parse));
            foreach (var pair in state.NotificationSettings)
            {
                var x = pair.Value;
                await UpsertAsync(connection, tx, "notification_settings",
                    ["user_id", "email_task", "email_billing", "browser_notice", "weekly_report", "updated_at"],
                    [int.Parse(pair.Key), x.EmailTask, x.EmailBilling, x.BrowserNotice, x.WeeklyReport, x.UpdatedAt]);
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    async Task<bool> HasAnyDataAsync(MySqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM app_counters";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    async Task<MaskFlowState?> TryLoadLegacyStateAsync(MySqlConnection connection)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'app_state'";
        if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return null;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT state_json FROM app_state WHERE id = 1";
        var value = await command.ExecuteScalarAsync() as string;
        return string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<MaskFlowState>(value, json);
    }

    async Task LoadCountersAsync(MySqlConnection c, MaskFlowState s)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT next_user_id,next_file_id,next_event_id FROM app_counters WHERE id=1";
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return;
        s.NextUserId = r.GetInt32(0);
        s.NextFileId = r.GetInt32(1);
        s.NextEventId = r.GetInt32(2);
    }

    async Task LoadUsersAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM users", r =>
        s.Users.Add(new User(I(r, "id"), S(r, "email"), S(r, "password_hash"), S(r, "salt"), S(r, "plan"), L(r, "quota_bytes"), L(r, "used_bytes"), D(r, "created_at"), N(r, "username"), N(r, "phone"), N(r, "avatar_path"))));

    async Task LoadSessionsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM sessions", r =>
        s.Sessions.Add(new Session(S(r, "token"), I(r, "user_id"), D(r, "created_at"), D(r, "expires_at"), N(r, "device_id"))));

    async Task LoadProjectsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM projects", r =>
        s.Projects.Add(new Project(S(r, "id"), I(r, "user_id"), S(r, "name"), S(r, "description"), S(r, "data_type"), FromJson<SplitConfig>(S(r, "split_json"))!, I(r, "image_count"), I(r, "annotation_count"), D(r, "created_at"), D(r, "updated_at"))));

    async Task LoadProjectLabelsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM project_labels ORDER BY sort_order", r =>
    {
        var id = S(r, "project_id");
        if (!s.ProjectLabels.TryGetValue(id, out var labels))
        {
            labels = [];
            s.ProjectLabels[id] = labels;
        }
        labels.Add(S(r, "label_name"));
    });

    async Task LoadFilesAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM files", r =>
        s.Files.Add(new FileItem(I(r, "id"), I(r, "user_id"), N(r, "project_id"), S(r, "name"), S(r, "object_path"), L(r, "size_bytes"), S(r, "kind"), N(r, "content_type"), D(r, "created_at"), S(r, "download_url"))));

    async Task LoadAnnotationSetsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM annotation_sets", r =>
        s.AnnotationSets.Add(new AnnotationSet(S(r, "id"), I(r, "user_id"), I(r, "file_id"), I(r, "width"), I(r, "height"), FromJson<List<AnnotationItem>>(S(r, "annotations_json")) ?? [], S(r, "yolo_txt"), D(r, "created_at"), D(r, "updated_at"))));

    async Task LoadTasksAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM tasks", r =>
        s.Tasks.Add(new TaskItem(S(r, "id"), I(r, "user_id"), S(r, "job_id"), S(r, "type"), N(r, "title"), N(r, "project_id"), NI(r, "file_id"), I(r, "image_count"), S(r, "status"), Db(r, "progress"), FromJson<Dictionary<string, object?>>(N(r, "result_json")), N(r, "error_message"), D(r, "created_at"), ND(r, "started_at"), ND(r, "finished_at"))));

    async Task LoadJobsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM jobs", r =>
        s.Jobs.Add(new Job(S(r, "id"), S(r, "app"), S(r, "type"), I(r, "user_id"), N(r, "project_id"), S(r, "pool"), S(r, "priority"), S(r, "status"), FromJson<Dictionary<string, object?>>(S(r, "resources_json")) ?? [], FromJson<Dictionary<string, object?>>(S(r, "input_json")) ?? [], FromJson<Dictionary<string, object?>>(N(r, "output_json")) , FromJson<Dictionary<string, object?>>(S(r, "params_json")) ?? [], N(r, "node_id"), NI(r, "reserved_credits"), NI(r, "charged_credits"), N(r, "error"), D(r, "created_at"), ND(r, "started_at"), ND(r, "finished_at"))));

    async Task LoadJobEventsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM job_events", r =>
        s.JobEvents.Add(new JobEvent(I(r, "id"), S(r, "job_id"), S(r, "event_type"), FromJson<Dictionary<string, object?>>(S(r, "payload_json")) ?? [], D(r, "created_at"))));

    async Task LoadNodesAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM nodes", r =>
        s.Nodes.Add(new Node(S(r, "id"), I(r, "owner_id"), S(r, "pool"), S(r, "status"), N(r, "gpu_model"), NI(r, "vram_gb"), N(r, "region"), NDb(r, "price_per_hour"), Db(r, "reputation"), S(r, "api_key"), D(r, "created_at"), ND(r, "approved_at"), ND(r, "last_heartbeat"))));

    async Task LoadPoolsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM pools", r =>
        s.Pools.Add(new Pool(S(r, "id"), S(r, "name"), S(r, "type"), N(r, "region"), S(r, "status"), FromJson<Dictionary<string, object?>>(S(r, "capacity_json")) ?? [], FromJson<Dictionary<string, object?>>(S(r, "policy_json")) ?? [], D(r, "created_at"), D(r, "updated_at"))));

    async Task LoadPricingRulesAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM pricing_rules", r =>
        s.PricingRules.Add(new PricingRule(S(r, "id"), S(r, "name"), S(r, "resource_type"), N(r, "pool"), N(r, "region"), Db(r, "unit_price"), S(r, "billing_unit"), S(r, "status"), D(r, "effective_at"), D(r, "updated_at"))));

    async Task LoadWalletLedgerAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM wallet_ledger", r =>
        s.WalletLedger.Add(new WalletEntry(I(r, "id"), I(r, "user_id"), I(r, "delta"), S(r, "reason"), N(r, "job_id"), D(r, "created_at"))));

    async Task LoadSettlementsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM settlements", r =>
        s.Settlements.Add(new Settlement(S(r, "id"), I(r, "provider_id"), S(r, "period"), I(r, "node_count"), Db(r, "gross_amount"), Db(r, "platform_fee"), Db(r, "net_amount"), S(r, "status"), D(r, "created_at"), ND(r, "paid_at"))));

    async Task LoadApiTokensAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM api_tokens", r =>
        s.ApiTokens.Add(new ApiToken(S(r, "id"), I(r, "user_id"), S(r, "name"), S(r, "token_hash"), S(r, "token_prefix"), D(r, "created_at"), ND(r, "last_used_at"), ND(r, "revoked_at"))));

    async Task LoadTeamMembersAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM team_members", r =>
        s.TeamMembers.Add(new TeamMember(S(r, "id"), I(r, "user_id"), S(r, "email"), S(r, "role"), S(r, "status"), D(r, "created_at"))));

    async Task LoadDevicesAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM account_devices", r =>
        s.Devices.Add(new AccountDevice(S(r, "id"), I(r, "user_id"), S(r, "name"), N(r, "ip"), N(r, "user_agent"), D(r, "created_at"), D(r, "last_seen_at"), ND(r, "revoked_at"))));

    async Task LoadExportsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM dataset_exports", r =>
        s.Exports.Add(new DatasetExport(S(r, "id"), I(r, "user_id"), N(r, "project_id"), N(r, "task_id"), S(r, "status"), N(r, "object_path"), L(r, "size_bytes"), FromJson<ExportRequest>(S(r, "config_json"))!, D(r, "created_at"), ND(r, "finished_at"), N(r, "error_message"), N(r, "download_url"))));

    async Task LoadQuotasAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM ai_quotas", r =>
        s.Quotas[I(r, "user_id").ToString()] = new AiQuota(S(r, "plan"), I(r, "daily_limit"), I(r, "daily_used"), DateOnly.FromDateTime(r.GetDateTime("daily_reset_at"))));

    async Task LoadNotificationsAsync(MySqlConnection c, MaskFlowState s) => await ReadAsync(c, "SELECT * FROM notification_settings", r =>
        s.NotificationSettings[I(r, "user_id").ToString()] = new NotificationSettings(B(r, "email_task"), B(r, "email_billing"), B(r, "browser_notice"), B(r, "weekly_report"), D(r, "updated_at")));

    async Task ReadAsync(MySqlConnection connection, string sql, Action<MySqlDataReader> read)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) read(reader);
    }

    async Task UpsertAsync(MySqlConnection connection, MySqlTransaction tx, string table, string[] columns, object?[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        var columnNames = string.Join(", ", columns.Select(column => $"`{column}`"));
        var placeholders = string.Join(", ", columns.Select((_, i) => "@p" + i));
        var updates = string.Join(", ", columns.Select(column => $"`{column}`=VALUES(`{column}`)"));
        command.CommandText = $"INSERT INTO `{table}` ({columnNames}) VALUES ({placeholders}) ON DUPLICATE KEY UPDATE {updates}";
        for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    async Task PruneExceptAsync<T>(MySqlConnection connection, MySqlTransaction tx, string table, string keyColumn, IEnumerable<T> keepKeys)
    {
        var keys = keepKeys.ToList();
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        if (keys.Count == 0)
        {
            command.CommandText = $"DELETE FROM `{table}`";
            await command.ExecuteNonQueryAsync();
            return;
        }

        var placeholders = keys.Select((_, i) => "@k" + i).ToArray();
        command.CommandText = $"DELETE FROM `{table}` WHERE `{keyColumn}` NOT IN ({string.Join(", ", placeholders)})";
        for (var i = 0; i < keys.Count; i++) command.Parameters.AddWithValue("@k" + i, keys[i]);
        await command.ExecuteNonQueryAsync();
    }

    async Task SyncProjectLabelsAsync(MySqlConnection connection, MySqlTransaction tx, MaskFlowState state, IReadOnlyCollection<string> projectIds)
    {
        foreach (var projectId in projectIds)
        {
            var labels = state.ProjectLabels.TryGetValue(projectId, out var stored) ? stored : [];
            if (labels.Count == 0)
            {
                await using var clear = connection.CreateCommand();
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM `project_labels` WHERE `project_id` = @project_id";
                clear.Parameters.AddWithValue("@project_id", projectId);
                await clear.ExecuteNonQueryAsync();
                continue;
            }

            await using var prune = connection.CreateCommand();
            prune.Transaction = tx;
            var placeholders = labels.Select((_, i) => "@label" + i).ToArray();
            prune.CommandText = $"DELETE FROM `project_labels` WHERE `project_id` = @project_id AND `label_name` NOT IN ({string.Join(", ", placeholders)})";
            prune.Parameters.AddWithValue("@project_id", projectId);
            for (var i = 0; i < labels.Count; i++) prune.Parameters.AddWithValue("@label" + i, labels[i]);
            await prune.ExecuteNonQueryAsync();

            for (var i = 0; i < labels.Count; i++)
            {
                await UpsertAsync(connection, tx, "project_labels",
                    ["project_id", "label_name", "sort_order"],
                    [projectId, labels[i], i]);
            }
        }
    }

    static async Task ExecAsync(MySqlConnection connection, MySqlTransaction? tx, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    string Json<T>(T value) => JsonSerializer.Serialize(value, json);
    string? JsonOrNull<T>(T? value) => value is null ? null : JsonSerializer.Serialize(value, json);
    T? FromJson<T>(string? value) => string.IsNullOrWhiteSpace(value) ? default : JsonSerializer.Deserialize<T>(value, json);

    static string S(MySqlDataReader r, string name) => r.GetString(name);
    static string? N(MySqlDataReader r, string name) => r.IsDBNull(name) ? null : r.GetString(name);
    static int I(MySqlDataReader r, string name) => r.GetInt32(name);
    static int? NI(MySqlDataReader r, string name) => r.IsDBNull(name) ? null : r.GetInt32(name);
    static long L(MySqlDataReader r, string name) => r.GetInt64(name);
    static double Db(MySqlDataReader r, string name) => r.GetDouble(name);
    static double? NDb(MySqlDataReader r, string name) => r.IsDBNull(name) ? null : r.GetDouble(name);
    static bool B(MySqlDataReader r, string name) => r.GetBoolean(name);
    static DateTimeOffset D(MySqlDataReader r, string name) => new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(name), DateTimeKind.Utc));
    static DateTimeOffset? ND(MySqlDataReader r, string name) => r.IsDBNull(name) ? null : D(r, name);

    static readonly string[] SchemaSql =
    [
        """
        CREATE TABLE IF NOT EXISTS app_counters (
          id INT PRIMARY KEY,
          next_user_id INT NOT NULL,
          next_file_id INT NOT NULL,
          next_event_id INT NOT NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS users (
          id INT PRIMARY KEY,
          email VARCHAR(255) NOT NULL UNIQUE,
          password_hash VARCHAR(255) NOT NULL,
          salt VARCHAR(64) NOT NULL,
          plan VARCHAR(32) NOT NULL,
          quota_bytes BIGINT NOT NULL,
          used_bytes BIGINT NOT NULL,
          created_at DATETIME(6) NOT NULL,
          username VARCHAR(120) NULL,
          phone VARCHAR(40) NULL,
          avatar_path VARCHAR(1024) NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS sessions (
          token VARCHAR(255) PRIMARY KEY,
          user_id INT NOT NULL,
          created_at DATETIME(6) NOT NULL,
          expires_at DATETIME(6) NOT NULL,
          INDEX ix_sessions_user_id (user_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        "ALTER TABLE sessions ADD COLUMN expires_at DATETIME(6) NOT NULL DEFAULT (DATE_ADD(NOW(), INTERVAL 7 DAY))",
        "ALTER TABLE sessions ADD COLUMN device_id VARCHAR(64) NULL",
        """
        CREATE TABLE IF NOT EXISTS projects (
          id VARCHAR(64) PRIMARY KEY,
          user_id INT NOT NULL,
          name VARCHAR(200) NOT NULL,
          description TEXT NOT NULL,
          data_type VARCHAR(64) NOT NULL,
          split_json JSON NOT NULL,
          image_count INT NOT NULL,
          annotation_count INT NOT NULL,
          created_at DATETIME(6) NOT NULL,
          updated_at DATETIME(6) NOT NULL,
          INDEX ix_projects_user_id (user_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS project_labels (
          project_id VARCHAR(64) NOT NULL,
          label_name VARCHAR(120) NOT NULL,
          sort_order INT NOT NULL,
          PRIMARY KEY (project_id, label_name)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS files (
          id INT PRIMARY KEY,
          user_id INT NOT NULL,
          project_id VARCHAR(64) NULL,
          name VARCHAR(255) NOT NULL,
          object_path VARCHAR(1200) NOT NULL,
          size_bytes BIGINT NOT NULL,
          kind VARCHAR(32) NOT NULL,
          content_type VARCHAR(120) NULL,
          created_at DATETIME(6) NOT NULL,
          download_url VARCHAR(500) NOT NULL,
          INDEX ix_files_user_project (user_id, project_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS annotation_sets (
          id VARCHAR(64) PRIMARY KEY,
          user_id INT NOT NULL,
          file_id INT NOT NULL,
          width INT NOT NULL,
          height INT NOT NULL,
          annotations_json JSON NOT NULL,
          yolo_txt MEDIUMTEXT NOT NULL,
          created_at DATETIME(6) NOT NULL,
          updated_at DATETIME(6) NOT NULL,
          UNIQUE KEY ux_annotation_file_user (user_id, file_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS tasks (
          id VARCHAR(64) PRIMARY KEY,
          user_id INT NOT NULL,
          job_id VARCHAR(64) NOT NULL,
          type VARCHAR(80) NOT NULL,
          title VARCHAR(255) NULL,
          project_id VARCHAR(64) NULL,
          file_id INT NULL,
          image_count INT NOT NULL,
          status VARCHAR(40) NOT NULL,
          progress DOUBLE NOT NULL,
          result_json JSON NULL,
          error_message TEXT NULL,
          created_at DATETIME(6) NOT NULL,
          started_at DATETIME(6) NULL,
          finished_at DATETIME(6) NULL,
          INDEX ix_tasks_user_project (user_id, project_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS jobs (
          id VARCHAR(64) PRIMARY KEY,
          app VARCHAR(80) NOT NULL,
          type VARCHAR(120) NOT NULL,
          user_id INT NOT NULL,
          project_id VARCHAR(64) NULL,
          pool VARCHAR(120) NOT NULL,
          priority VARCHAR(40) NOT NULL,
          status VARCHAR(40) NOT NULL,
          resources_json JSON NOT NULL,
          input_json JSON NOT NULL,
          output_json JSON NULL,
          params_json JSON NOT NULL,
          node_id VARCHAR(64) NULL,
          reserved_credits INT NULL,
          charged_credits INT NULL,
          error TEXT NULL,
          created_at DATETIME(6) NOT NULL,
          started_at DATETIME(6) NULL,
          finished_at DATETIME(6) NULL,
          INDEX ix_jobs_status (status)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS job_events (
          id INT PRIMARY KEY,
          job_id VARCHAR(64) NOT NULL,
          event_type VARCHAR(80) NOT NULL,
          payload_json JSON NOT NULL,
          created_at DATETIME(6) NOT NULL,
          INDEX ix_job_events_job_id (job_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS nodes (
          id VARCHAR(64) PRIMARY KEY,
          owner_id INT NOT NULL,
          pool VARCHAR(120) NOT NULL,
          status VARCHAR(40) NOT NULL,
          gpu_model VARCHAR(120) NULL,
          vram_gb INT NULL,
          region VARCHAR(80) NULL,
          price_per_hour DOUBLE NULL,
          reputation DOUBLE NOT NULL,
          api_key VARCHAR(255) NOT NULL,
          created_at DATETIME(6) NOT NULL,
          approved_at DATETIME(6) NULL,
          last_heartbeat DATETIME(6) NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS pools (
          id VARCHAR(64) PRIMARY KEY,
          name VARCHAR(160) NOT NULL,
          type VARCHAR(80) NOT NULL,
          region VARCHAR(80) NULL,
          status VARCHAR(40) NOT NULL,
          capacity_json JSON NOT NULL,
          policy_json JSON NOT NULL,
          created_at DATETIME(6) NOT NULL,
          updated_at DATETIME(6) NOT NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS pricing_rules (
          id VARCHAR(64) PRIMARY KEY,
          name VARCHAR(160) NOT NULL,
          resource_type VARCHAR(80) NOT NULL,
          pool VARCHAR(120) NULL,
          region VARCHAR(80) NULL,
          unit_price DOUBLE NOT NULL,
          billing_unit VARCHAR(40) NOT NULL,
          status VARCHAR(40) NOT NULL,
          effective_at DATETIME(6) NOT NULL,
          updated_at DATETIME(6) NOT NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS wallet_ledger (
          id INT PRIMARY KEY,
          user_id INT NOT NULL,
          delta INT NOT NULL,
          reason VARCHAR(255) NOT NULL,
          job_id VARCHAR(64) NULL,
          created_at DATETIME(6) NOT NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS settlements (
          id VARCHAR(64) PRIMARY KEY,
          provider_id INT NOT NULL,
          period VARCHAR(40) NOT NULL,
          node_count INT NOT NULL,
          gross_amount DOUBLE NOT NULL,
          platform_fee DOUBLE NOT NULL,
          net_amount DOUBLE NOT NULL,
          status VARCHAR(40) NOT NULL,
          created_at DATETIME(6) NOT NULL,
          paid_at DATETIME(6) NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS api_tokens (
          id VARCHAR(64) PRIMARY KEY,
          user_id INT NOT NULL,
          name VARCHAR(120) NOT NULL,
          token_hash VARCHAR(255) NOT NULL,
          token_prefix VARCHAR(20) NOT NULL,
          created_at DATETIME(6) NOT NULL,
          last_used_at DATETIME(6) NULL,
          revoked_at DATETIME(6) NULL,
          INDEX ix_api_tokens_user_id (user_id)
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS team_members (
          id VARCHAR(64) PRIMARY KEY,
          user_id INT NOT NULL,
          email VARCHAR(255) NOT NULL,
          role VARCHAR(40) NOT NULL,
          status VARCHAR(40) NOT NULL,
          created_at DATETIME(6) NOT NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS account_devices (
          id VARCHAR(64) PRIMARY KEY,
          user_id INT NOT NULL,
          name VARCHAR(160) NOT NULL,
          ip VARCHAR(80) NULL,
          user_agent TEXT NULL,
          created_at DATETIME(6) NOT NULL,
          last_seen_at DATETIME(6) NOT NULL,
          revoked_at DATETIME(6) NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS dataset_exports (
          id VARCHAR(64) PRIMARY KEY,
          user_id INT NOT NULL,
          project_id VARCHAR(64) NULL,
          task_id VARCHAR(64) NULL,
          status VARCHAR(40) NOT NULL,
          object_path VARCHAR(1200) NULL,
          size_bytes BIGINT NOT NULL,
          config_json JSON NOT NULL,
          created_at DATETIME(6) NOT NULL,
          finished_at DATETIME(6) NULL,
          error_message TEXT NULL,
          download_url VARCHAR(500) NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS ai_quotas (
          user_id INT PRIMARY KEY,
          plan VARCHAR(32) NOT NULL,
          daily_limit INT NOT NULL,
          daily_used INT NOT NULL,
          daily_reset_at DATE NOT NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS notification_settings (
          user_id INT PRIMARY KEY,
          email_task BOOLEAN NOT NULL,
          email_billing BOOLEAN NOT NULL,
          browser_notice BOOLEAN NOT NULL,
          weekly_report BOOLEAN NOT NULL,
          updated_at DATETIME(6) NOT NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        """
    ];
}
