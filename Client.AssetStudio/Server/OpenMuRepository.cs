using Npgsql;

namespace Client.AssetStudio.Server;

/// <summary>OpenMU 的 <c>config.MonsterDefinition</c> 一列。</summary>
public sealed class MonsterRow
{
    public required Guid Id { get; init; }

    /// <summary>與客戶端 <c>[NpcInfo(typeId, …)]</c> 的 typeId 是同一個號碼。</summary>
    public required short Number { get; init; }

    public required string Designation { get; set; }

    public short MoveRange { get; set; }
    public short AttackRange { get; set; }
    public short ViewRange { get; set; }
    public TimeSpan MoveDelay { get; set; }
    public TimeSpan AttackDelay { get; set; }
    public TimeSpan RespawnDelay { get; set; }
    public short Attribute { get; set; }
    public int NumberOfMaximumItemDrops { get; set; }
    public string? IntelligenceTypeName { get; set; }

    /// <summary>屬性（HP、等級、傷害…）存在另一張表，鍵是 AttributeDefinition 的名稱。</summary>
    public List<MonsterAttributeRow> Attributes { get; } = [];

    public MonsterRow Clone()
    {
        var copy = new MonsterRow
        {
            Id = Id,
            Number = Number,
            Designation = Designation,
            MoveRange = MoveRange,
            AttackRange = AttackRange,
            ViewRange = ViewRange,
            MoveDelay = MoveDelay,
            AttackDelay = AttackDelay,
            RespawnDelay = RespawnDelay,
            Attribute = Attribute,
            NumberOfMaximumItemDrops = NumberOfMaximumItemDrops,
            IntelligenceTypeName = IntelligenceTypeName,
        };

        copy.Attributes.AddRange(Attributes.Select(a => a with { }));
        return copy;
    }
}

public sealed record MonsterAttributeRow(Guid Id, string Designation)
{
    public float Value { get; set; }
}

/// <summary>OpenMU 的 <c>config.Skill</c> 一列 —— 技能的傷害與判定的真相。</summary>
public sealed class SkillRow
{
    public required Guid Id { get; init; }
    public required short Number { get; init; }
    public required string Name { get; set; }
    public short Range { get; set; }
    public int DamageType { get; set; }
    public int SkillType { get; set; }
    public int Target { get; set; }
    public short ImplicitTargetRange { get; set; }
    public int AttackDamage { get; set; }
    public bool MovesToTarget { get; set; }
    public bool MovesTarget { get; set; }
    public short NumberOfHitsPerAttack { get; set; }
}

/// <summary>
/// 直接對 OpenMU 的 PostgreSQL 讀寫。
/// </summary>
/// <remarks>
/// <b>為什麼工具非碰資料庫不可：</b>外觀在客戶端，行為在伺服器。
/// <c>Monster33.bmd</c> 決定這隻怪長什麼樣、有哪些動作；
/// 它跑多快、打多遠、多少血、掉什麼，全部在這裡。兩邊靠
/// <c>MonsterDefinition.Number</c> ↔ <c>[NpcInfo(typeId, …)]</c> 對上。
/// 只給前者的工具會讓人改了半天 <c>.bmd</c>，然後困惑為什麼遊戲裡的攻擊速度沒變。
///
/// <b>刻意用原始 SQL 而不是接 OpenMU 的 Persistence 層：</b>
/// 那一層要跑 source generator、EF Core 與整個 <c>GameConfiguration</c> 物件圖，
/// 在主機上光是建置就會失敗（<c>Dart Sass not installed</c>，見 HANDOFF 第 3 節）。
/// 這裡只要讀寫幾個欄位。
///
/// <b>寫入之後要重啟伺服器。</b>OpenMU 在啟動時把 <c>GameConfiguration</c> 整份讀進記憶體，
/// 執行中的容器不會看到資料庫的變更。UI 必須把這件事講出來，否則使用者會以為工具壞了。
/// </remarks>
public sealed class OpenMuRepository
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5433;Database=openmu;Username=postgres;Password=admin;Timeout=5;Command Timeout=15";

    private string _connectionString = DefaultConnectionString;

    public string ConnectionString
    {
        get => _connectionString;
        set
        {
            _connectionString = value;
            IsConnected = false;
        }
    }

    public bool IsConnected { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>寫入的安全開關。預設關閉 —— 這是一個活著的遊戲資料庫。</summary>
    public bool WriteEnabled { get; set; }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await using var connection = await OpenAsync();
            IsConnected = true;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            LastError = ex.Message;
            return false;
        }
    }

    // ── 怪物 ─────────────────────────────────────────────────────

    public async Task<Dictionary<short, MonsterRow>> LoadMonstersAsync()
    {
        var monsters = new Dictionary<short, MonsterRow>();

        await using var connection = await OpenAsync();

        await using (var command = new NpgsqlCommand(
            """
            SELECT "Id", "Number", "Designation", "MoveRange", "AttackRange", "ViewRange",
                   "MoveDelay", "AttackDelay", "RespawnDelay", "Attribute",
                   "NumberOfMaximumItemDrops", "IntelligenceTypeName"
            FROM config."MonsterDefinition"
            ORDER BY "Number"
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var row = new MonsterRow
                {
                    Id = reader.GetGuid(0),
                    Number = reader.GetInt16(1),
                    Designation = reader.GetString(2),
                    MoveRange = reader.GetInt16(3),
                    AttackRange = reader.GetInt16(4),
                    ViewRange = reader.GetInt16(5),
                    MoveDelay = reader.GetTimeSpan(6),
                    AttackDelay = reader.GetTimeSpan(7),
                    RespawnDelay = reader.GetTimeSpan(8),
                    Attribute = reader.GetInt16(9),
                    NumberOfMaximumItemDrops = reader.GetInt32(10),
                    IntelligenceTypeName = reader.IsDBNull(11) ? null : reader.GetString(11),
                };

                // 同一個 Number 出現兩次的話後者覆蓋前者 —— 與 OpenMU 自己的查表行為一致。
                monsters[row.Number] = row;
            }
        }

        await using (var command = new NpgsqlCommand(
            """
            SELECT md."Number", ma."Id", ad."Designation", ma."Value"
            FROM config."MonsterAttribute" ma
            JOIN config."MonsterDefinition" md ON md."Id" = ma."MonsterDefinitionId"
            JOIN config."AttributeDefinition" ad ON ad."Id" = ma."AttributeDefinitionId"
            ORDER BY md."Number", ad."Designation"
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (!monsters.TryGetValue(reader.GetInt16(0), out var monster))
                    continue;

                monster.Attributes.Add(new MonsterAttributeRow(reader.GetGuid(1), reader.GetString(2))
                {
                    Value = reader.GetFloat(3),
                });
            }
        }

        IsConnected = true;
        LastError = null;
        return monsters;
    }

    public async Task SaveMonsterAsync(MonsterRow row)
    {
        RequireWrite();

        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var command = new NpgsqlCommand(
            """
            UPDATE config."MonsterDefinition"
            SET "Designation" = @designation,
                "MoveRange" = @moveRange,
                "AttackRange" = @attackRange,
                "ViewRange" = @viewRange,
                "MoveDelay" = @moveDelay,
                "AttackDelay" = @attackDelay,
                "RespawnDelay" = @respawnDelay,
                "Attribute" = @attribute,
                "NumberOfMaximumItemDrops" = @drops,
                "IntelligenceTypeName" = @intelligence
            WHERE "Id" = @id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("designation", row.Designation);
            command.Parameters.AddWithValue("moveRange", row.MoveRange);
            command.Parameters.AddWithValue("attackRange", row.AttackRange);
            command.Parameters.AddWithValue("viewRange", row.ViewRange);
            command.Parameters.AddWithValue("moveDelay", row.MoveDelay);
            command.Parameters.AddWithValue("attackDelay", row.AttackDelay);
            command.Parameters.AddWithValue("respawnDelay", row.RespawnDelay);
            command.Parameters.AddWithValue("attribute", row.Attribute);
            command.Parameters.AddWithValue("drops", row.NumberOfMaximumItemDrops);
            command.Parameters.AddWithValue("intelligence", (object?)row.IntelligenceTypeName ?? DBNull.Value);
            command.Parameters.AddWithValue("id", row.Id);

            await command.ExecuteNonQueryAsync();
        }

        foreach (var attribute in row.Attributes)
        {
            await using var command = new NpgsqlCommand(
                """UPDATE config."MonsterAttribute" SET "Value" = @value WHERE "Id" = @id""",
                connection, transaction);

            command.Parameters.AddWithValue("value", attribute.Value);
            command.Parameters.AddWithValue("id", attribute.Id);

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    // ── 技能 ─────────────────────────────────────────────────────

    public async Task<Dictionary<short, SkillRow>> LoadSkillsAsync()
    {
        var skills = new Dictionary<short, SkillRow>();

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT "Id", "Number", "Name", "Range", "DamageType", "SkillType", "Target",
                   "ImplicitTargetRange", "AttackDamage", "MovesToTarget", "MovesTarget",
                   "NumberOfHitsPerAttack"
            FROM config."Skill"
            ORDER BY "Number"
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new SkillRow
            {
                Id = reader.GetGuid(0),
                Number = reader.GetInt16(1),
                Name = reader.GetString(2),
                Range = reader.GetInt16(3),
                DamageType = reader.GetInt32(4),
                SkillType = reader.GetInt32(5),
                Target = reader.GetInt32(6),
                ImplicitTargetRange = reader.GetInt16(7),
                AttackDamage = reader.GetInt32(8),
                MovesToTarget = reader.GetBoolean(9),
                MovesTarget = reader.GetBoolean(10),
                NumberOfHitsPerAttack = reader.GetInt16(11),
            };

            skills[row.Number] = row;
        }

        IsConnected = true;
        LastError = null;
        return skills;
    }

    public async Task SaveSkillAsync(SkillRow row)
    {
        RequireWrite();

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            UPDATE config."Skill"
            SET "Name" = @name,
                "Range" = @range,
                "DamageType" = @damageType,
                "SkillType" = @skillType,
                "Target" = @target,
                "ImplicitTargetRange" = @implicitRange,
                "AttackDamage" = @damage,
                "MovesToTarget" = @movesToTarget,
                "MovesTarget" = @movesTarget,
                "NumberOfHitsPerAttack" = @hits
            WHERE "Id" = @id
            """, connection);

        command.Parameters.AddWithValue("name", row.Name);
        command.Parameters.AddWithValue("range", row.Range);
        command.Parameters.AddWithValue("damageType", row.DamageType);
        command.Parameters.AddWithValue("skillType", row.SkillType);
        command.Parameters.AddWithValue("target", row.Target);
        command.Parameters.AddWithValue("implicitRange", row.ImplicitTargetRange);
        command.Parameters.AddWithValue("damage", row.AttackDamage);
        command.Parameters.AddWithValue("movesToTarget", row.MovesToTarget);
        command.Parameters.AddWithValue("movesTarget", row.MovesTarget);
        command.Parameters.AddWithValue("hits", row.NumberOfHitsPerAttack);
        command.Parameters.AddWithValue("id", row.Id);

        await command.ExecuteNonQueryAsync();
    }

    // ── 生怪區（唯讀，用來回答「這隻怪出現在哪」）────────────────

    public async Task<Dictionary<short, List<string>>> LoadSpawnSummaryAsync()
    {
        var spawns = new Dictionary<short, List<string>>();

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT md."Number", gmd."Name", COUNT(*), SUM(msa."Quantity")
            FROM config."MonsterSpawnArea" msa
            JOIN config."MonsterDefinition" md ON md."Id" = msa."MonsterDefinitionId"
            LEFT JOIN config."GameMapDefinition" gmd ON gmd."Id" = msa."GameMapId"
            GROUP BY md."Number", gmd."Name"
            ORDER BY md."Number", gmd."Name"
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            short number = reader.GetInt16(0);
            string map = reader.IsDBNull(1) ? "（未指定地圖）" : reader.GetString(1);
            long areas = reader.GetInt64(2);
            long quantity = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);

            if (!spawns.TryGetValue(number, out var list))
                spawns[number] = list = [];

            list.Add($"{map}：{areas} 區、共 {quantity} 隻");
        }

        return spawns;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private void RequireWrite()
    {
        if (!WriteEnabled)
            throw new InvalidOperationException("寫入未啟用。這是活著的遊戲資料庫，請先在伺服器面板打開寫入開關。");
    }
}
