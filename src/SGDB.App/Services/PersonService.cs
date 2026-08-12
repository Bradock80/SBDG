using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public sealed class PersonInput
{
    public string PersonKind { get; set; } = "juridica";
    public required string Name { get; set; }
    public string? TradeName { get; set; }
    public string? CpfCnpj { get; set; }
    public string? RgIe { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Cell1 { get; set; }
    public string? Whatsapp { get; set; }
    public string? Cell2 { get; set; }
    public string? Email { get; set; }
    public string? Cep { get; set; }
    public string? Address { get; set; }
    public string? AddressNumber { get; set; }
    public string? Complement { get; set; }
    public string? Neighborhood { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ReceiptType { get; set; }
    public PersonRoles Roles { get; set; } = PersonRoles.ForNewCliente();
    public string? Notes { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>Acréscimo por unidade no fiado (0 = off).</summary>
    public double FiadoUnitSurcharge { get; set; }
}

public static class PersonService
{
    public static IReadOnlyList<Person> List(string? search = null, string ativo = "ativos", string tipo = "clientes")
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListPeople(search, ativo, tipo);
        return ListLocal(search, ativo, tipo);
    }

    public static IReadOnlyList<Person> ListLocal(string? search = null, string ativo = "ativos", string tipo = "clientes")
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();

        var sql = """
            SELECT id, person_kind, name, trade_name, cpf_cnpj, rg_ie,
                   phone, phone2, cell1, whatsapp, cell2, email,
                   cep, address, address_number, complement, neighborhood, city, state,
                   receipt_type, roles_json, notes, active, created_at,
                   IFNULL(fiado_unit_surcharge, 0)
            FROM people
            WHERE 1=1
            """;

        if (ativo == "ativos")
            sql += " AND active = 1";
        else if (ativo == "inativos")
            sql += " AND active = 0";

        if (tipo == "clientes")
            sql += " AND CAST(json_extract(roles_json, '$.clientes') AS INTEGER) = 1";
        else if (tipo == "fornecedores")
            sql += " AND CAST(json_extract(roles_json, '$.fornecedores') AS INTEGER) = 1";

        if (!string.IsNullOrWhiteSpace(search))
        {
            var raw = search.Trim();
            sql += """
                 AND (
                    UPPER(name) LIKE $like ESCAPE '\'
                    OR UPPER(IFNULL(trade_name,'')) LIKE $like ESCAPE '\'
                    OR IFNULL(cpf_cnpj,'') LIKE $like ESCAPE '\'
                 )
                """;
            var escaped = raw.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            cmd.Parameters.AddWithValue("$like", $"%{escaped.ToUpperInvariant()}%");
        }

        sql += " ORDER BY name LIMIT 1000";
        cmd.CommandText = sql;
        return ReadAll(cmd);
    }

    public static Person? GetById(int id)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetPerson(id);
        return GetByIdLocal(id);
    }

    public static Person? GetByIdLocal(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, person_kind, name, trade_name, cpf_cnpj, rg_ie,
                   phone, phone2, cell1, whatsapp, cell2, email,
                   cep, address, address_number, complement, neighborhood, city, state,
                   receipt_type, roles_json, notes, active, created_at,
                   IFNULL(fiado_unit_surcharge, 0)
            FROM people WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    public static Person? FindByDocumentDigits(string digits)
    {
        digits = new string((digits ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 11) return null;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, person_kind, name, trade_name, cpf_cnpj, rg_ie,
                   phone, phone2, cell1, whatsapp, cell2, email,
                   cep, address, address_number, complement, neighborhood, city, state,
                   receipt_type, roles_json, notes, active, created_at,
                   IFNULL(fiado_unit_surcharge, 0)
            FROM people
            WHERE REPLACE(REPLACE(REPLACE(IFNULL(cpf_cnpj,''),'.',''),'-',''),'/','') = $d
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$d", digits);
        return ReadAll(cmd).FirstOrDefault();
    }

    /// <summary>Busca pessoa (fornecedor/cliente) por CPF/CNPJ, comparando apenas os dígitos.</summary>
    public static Person? FindByCnpjDigits(string? cnpjOrCpf)
    {
        var digits = FormatDoc(cnpjOrCpf);
        if (digits is null)
            return null;
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.FindPersonByDoc(digits);
        return FindByCnpjDigitsLocal(digits);
    }

    public static Person? FindByCnpjDigitsLocal(string? cnpjOrCpf)
    {
        var digits = FormatDoc(cnpjOrCpf);
        if (digits is null)
            return null;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, person_kind, name, trade_name, cpf_cnpj, rg_ie,
                   phone, phone2, cell1, whatsapp, cell2, email,
                   cep, address, address_number, complement, neighborhood, city, state,
                   receipt_type, roles_json, notes, active, created_at,
                   IFNULL(fiado_unit_surcharge, 0)
            FROM people
            WHERE REPLACE(REPLACE(REPLACE(REPLACE(IFNULL(cpf_cnpj,''), '.', ''), '/', ''), '-', ''), ' ', '') = $doc
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$doc", digits);
        return ReadAll(cmd).FirstOrDefault();
    }

    public static Person Create(PersonInput input, bool requireClienteRole = true)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.CreatePerson(input, requireClienteRole);
        return CreateLocal(input, requireClienteRole);
    }

    public static Person CreateLocal(PersonInput input, bool requireClienteRole = true)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("criar pessoa");
        var data = Normalize(input, requireClienteRole);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (
                person_type, person_kind, name, trade_name, cpf_cnpj, rg_ie,
                phone, phone2, cell1, whatsapp, cell2, email,
                cep, address, address_number, complement, neighborhood, city, state,
                receipt_type, roles_json, notes, active, fiado_unit_surcharge, created_at
            ) VALUES (
                $person_type, $person_kind, $name, $trade_name, $cpf_cnpj, $rg_ie,
                $phone, $phone2, $cell1, $whatsapp, $cell2, $email,
                $cep, $address, $address_number, $complement, $neighborhood, $city, $state,
                $receipt_type, $roles_json, $notes, $active, $fiado_unit_surcharge, datetime('now','localtime')
            );
            SELECT last_insert_rowid();
            """;
        BindPerson(cmd, data);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        var person = GetByIdLocal(id) ?? throw new InvalidOperationException("Falha ao criar pessoa.");
        LogPersonAudit(person, isNew: true);
        return person;
    }

    public static Person Update(int id, PersonInput input, bool requireClienteRole = true)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.UpdatePerson(id, input, requireClienteRole);
        return UpdateLocal(id, input, requireClienteRole);
    }

    public static Person UpdateLocal(int id, PersonInput input, bool requireClienteRole = true)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("atualizar pessoa");
        var existing = GetByIdLocal(id) ?? throw new InvalidOperationException("Pessoa não encontrada.");
        var data = Normalize(input, requireClienteRole);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE people SET
                person_type = $person_type,
                person_kind = $person_kind,
                name = $name,
                trade_name = $trade_name,
                cpf_cnpj = $cpf_cnpj,
                rg_ie = $rg_ie,
                phone = $phone,
                phone2 = $phone2,
                cell1 = $cell1,
                whatsapp = $whatsapp,
                cell2 = $cell2,
                email = $email,
                cep = $cep,
                address = $address,
                address_number = $address_number,
                complement = $complement,
                neighborhood = $neighborhood,
                city = $city,
                state = $state,
                receipt_type = $receipt_type,
                roles_json = $roles_json,
                notes = $notes,
                active = $active,
                fiado_unit_surcharge = $fiado_unit_surcharge
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        BindPerson(cmd, data);
        cmd.ExecuteNonQuery();
        var updated = GetByIdLocal(id) ?? throw new InvalidOperationException("Falha ao atualizar pessoa.");
        LogPersonAudit(updated, isNew: false, existing);
        return updated;
    }

    private static void LogPersonAudit(Person person, bool isNew, Person? previous = null)
    {
        var entity = ResolvePersonEntity(person.Roles);
        if (isNew)
        {
            AuditService.LogJson("criar", entity, person.Id.ToString(),
                AuditPayloadBuilder.PersonChange(person.Id, person.Name, true),
                $"Cadastro de {entity}: {person.Name}");
            return;
        }

        if (previous is null)
            return;

        var changes = new Dictionary<string, object>();
        if (!string.Equals(previous.Name, person.Name, StringComparison.Ordinal))
            changes["nome"] = new { de = previous.Name, para = person.Name };
        if (!string.Equals(previous.CpfCnpj ?? "", person.CpfCnpj ?? "", StringComparison.Ordinal))
            changes["cpf_cnpj"] = new { de = previous.CpfCnpj ?? "—", para = person.CpfCnpj ?? "—" };
        if (!string.Equals(previous.RolesJson, person.RolesJson, StringComparison.Ordinal))
            changes["papeis"] = new { de = FormatRoles(previous.Roles), para = FormatRoles(person.Roles) };
        if (previous.Active != person.Active)
            changes["ativo"] = new { de = previous.Active, para = person.Active };
        if (changes.Count == 0)
            return;

        AuditService.LogJson("alterar", entity, person.Id.ToString(),
            AuditPayloadBuilder.PersonChange(person.Id, person.Name, false, changes),
            $"Alteração em {person.Name}: {string.Join(" · ", changes.Keys)}");
    }

    private static string ResolvePersonEntity(PersonRoles roles)
    {
        if (roles.Fornecedores && !roles.Clientes)
            return "fornecedor";
        if (roles.Clientes)
            return "cliente";
        return "pessoa";
    }

    private static string FormatRoles(PersonRoles roles)
    {
        var list = new List<string>();
        if (roles.Clientes) list.Add("Cliente");
        if (roles.Fornecedores) list.Add("Fornecedor");
        if (roles.Funcionarios) list.Add("Funcionário");
        return list.Count == 0 ? "—" : string.Join(", ", list);
    }

    public static void SoftDelete(int id)
    {
        if (StoreNetworkMode.IsClient)
        {
            // Sem endpoint DELETE: desativa via Update já roteado na Rede Loja.
            var person = GetById(id)
                ?? throw new InvalidOperationException("Pessoa não encontrada.");
            var roles = person.Roles;
            roles.Ativo = false;
            Update(id, new PersonInput
            {
                PersonKind = person.PersonKind,
                Name = person.Name,
                TradeName = person.TradeName,
                CpfCnpj = person.CpfCnpj,
                RgIe = person.RgIe,
                Phone = person.Phone,
                Phone2 = person.Phone2,
                Cell1 = person.Cell1,
                Whatsapp = person.Whatsapp,
                Cell2 = person.Cell2,
                Email = person.Email,
                Cep = person.Cep,
                Address = person.Address,
                AddressNumber = person.AddressNumber,
                Complement = person.Complement,
                Neighborhood = person.Neighborhood,
                City = person.City,
                State = person.State,
                ReceiptType = person.ReceiptType,
                Roles = roles,
                Notes = person.Notes,
                Active = false,
                FiadoUnitSurcharge = person.FiadoUnitSurcharge,
            }, requireClienteRole: false);
            return;
        }

        SoftDeleteLocal(id);
    }

    public static void SoftDeleteLocal(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("inativar pessoa");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE people SET active = 0 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Pessoa não encontrada.");
    }

    private static PersonInput Normalize(PersonInput input, bool requireClienteRole)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new InvalidOperationException("Informe o nome.");

        var roles = input.Roles;
        roles.Ativo = input.Active;
        if (requireClienteRole && !roles.Clientes)
            throw new InvalidOperationException("Marque o parâmetro Clientes para cadastrar nesta tela.");

        return new PersonInput
        {
            PersonKind = input.PersonKind is "fisica" or "juridica" ? input.PersonKind : "juridica",
            Name = TextNorm.UpperStr(input.Name) ?? "",
            TradeName = TextNorm.UpperStr(input.TradeName),
            CpfCnpj = FormatDoc(input.CpfCnpj),
            RgIe = TextNorm.UpperStr(input.RgIe),
            Phone = TextNorm.DigitsOnly(input.Phone, 16),
            Phone2 = TextNorm.DigitsOnly(input.Phone2, 16),
            Cell1 = TextNorm.DigitsOnly(input.Cell1, 16),
            Whatsapp = TextNorm.DigitsOnly(input.Whatsapp, 16),
            Cell2 = TextNorm.DigitsOnly(input.Cell2, 16),
            Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim().ToLowerInvariant(),
            Cep = TextNorm.DigitsOnly(input.Cep, 8),
            Address = TextNorm.UpperStr(input.Address),
            AddressNumber = TextNorm.UpperStr(input.AddressNumber),
            Complement = TextNorm.UpperStr(input.Complement),
            Neighborhood = TextNorm.UpperStr(input.Neighborhood),
            City = TextNorm.UpperStr(input.City),
            State = TextNorm.UpperState(input.State),
            ReceiptType = string.IsNullOrWhiteSpace(input.ReceiptType) ? null : input.ReceiptType.Trim().ToLowerInvariant(),
            Roles = roles,
            Notes = TextNorm.UpperStr(input.Notes),
            Active = input.Active,
            FiadoUnitSurcharge = Math.Max(0, ProductPriceHelper.RoundPrice(input.FiadoUnitSurcharge)),
        };
    }

    private static string? FormatDoc(string? value)
    {
        var digits = TextNorm.DigitsOnly(value, 14);
        if (digits is null)
            return null;
        return digits.Length <= 11
            ? digits.PadLeft(11, '0')[^11..]
            : digits.PadLeft(14, '0')[^14..];
    }

    private static void BindPerson(SqliteCommand cmd, PersonInput data)
    {
        cmd.Parameters.AddWithValue("$person_type", ResolvePersonType(data.Roles));
        cmd.Parameters.AddWithValue("$person_kind", data.PersonKind);
        cmd.Parameters.AddWithValue("$name", data.Name);
        cmd.Parameters.AddWithValue("$trade_name", (object?)data.TradeName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cpf_cnpj", (object?)data.CpfCnpj ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rg_ie", (object?)data.RgIe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$phone", (object?)data.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$phone2", (object?)data.Phone2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cell1", (object?)data.Cell1 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$whatsapp", (object?)data.Whatsapp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cell2", (object?)data.Cell2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$email", (object?)data.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cep", (object?)data.Cep ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$address", (object?)data.Address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$address_number", (object?)data.AddressNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$complement", (object?)data.Complement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$neighborhood", (object?)data.Neighborhood ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$city", (object?)data.City ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$state", (object?)data.State ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$receipt_type", (object?)data.ReceiptType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$roles_json", data.Roles.ToJson());
        cmd.Parameters.AddWithValue("$notes", (object?)data.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$active", data.Active ? 1 : 0);
        cmd.Parameters.AddWithValue("$fiado_unit_surcharge", data.FiadoUnitSurcharge);
    }

    /// <summary>Compatível com o enum do app web: CLIENTE | FORNECEDOR (maiúsculas).</summary>
    private static string ResolvePersonType(PersonRoles roles)
    {
        if (roles.Fornecedores && !roles.Clientes)
            return "FORNECEDOR";
        if (roles.Clientes && !roles.Fornecedores)
            return "CLIENTE";
        if (roles.Fornecedores)
            return "FORNECEDOR";
        return "CLIENTE";
    }

    private static List<Person> ReadAll(SqliteCommand cmd)
    {
        var list = new List<Person>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Person
            {
                Id = reader.GetInt32(0),
                PersonKind = reader.IsDBNull(1) ? "juridica" : reader.GetString(1),
                Name = reader.GetString(2),
                TradeName = reader.IsDBNull(3) ? null : reader.GetString(3),
                CpfCnpj = reader.IsDBNull(4) ? null : reader.GetString(4),
                RgIe = reader.IsDBNull(5) ? null : reader.GetString(5),
                Phone = reader.IsDBNull(6) ? null : reader.GetString(6),
                Phone2 = reader.IsDBNull(7) ? null : reader.GetString(7),
                Cell1 = reader.IsDBNull(8) ? null : reader.GetString(8),
                Whatsapp = reader.IsDBNull(9) ? null : reader.GetString(9),
                Cell2 = reader.IsDBNull(10) ? null : reader.GetString(10),
                Email = reader.IsDBNull(11) ? null : reader.GetString(11),
                Cep = reader.IsDBNull(12) ? null : reader.GetString(12),
                Address = reader.IsDBNull(13) ? null : reader.GetString(13),
                AddressNumber = reader.IsDBNull(14) ? null : reader.GetString(14),
                Complement = reader.IsDBNull(15) ? null : reader.GetString(15),
                Neighborhood = reader.IsDBNull(16) ? null : reader.GetString(16),
                City = reader.IsDBNull(17) ? null : reader.GetString(17),
                State = reader.IsDBNull(18) ? null : reader.GetString(18),
                ReceiptType = reader.IsDBNull(19) ? null : reader.GetString(19),
                RolesJson = reader.IsDBNull(20) ? "{}" : reader.GetString(20),
                Notes = reader.IsDBNull(21) ? null : reader.GetString(21),
                Active = !reader.IsDBNull(22) && reader.GetInt32(22) != 0,
                CreatedAt = reader.IsDBNull(23) ? "" : reader.GetString(23),
                FiadoUnitSurcharge = reader.FieldCount > 24 && !reader.IsDBNull(24)
                    ? reader.GetDouble(24)
                    : 0,
            });
        }
        return list;
    }
}
