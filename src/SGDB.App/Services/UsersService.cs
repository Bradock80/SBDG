using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

public sealed class UsersException : Exception
{
    public UsersException(string message) : base(message) { }
}

public static class UsersService
{
    public static IReadOnlyList<SystemUserRow> List(string? search = null, string ativo = "ativos")
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT id, login, nome, role, active, created_at, IFNULL(permissions_json,'')
            FROM users WHERE 1=1
            """;
        if (ativo == "ativos")
            sql += " AND active = 1";
        else if (ativo == "inativos")
            sql += " AND active = 0";

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (login LIKE $q OR nome LIKE $q)";
            cmd.Parameters.AddWithValue("$q", "%" + search.Trim() + "%");
        }
        sql += " ORDER BY active DESC, nome;";
        cmd.CommandText = sql;

        var list = new List<SystemUserRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var role = reader.GetString(3);
            list.Add(new SystemUserRow
            {
                Id = reader.GetInt32(0),
                Login = reader.GetString(1),
                Nome = reader.GetString(2),
                Role = role,
                Active = reader.GetInt32(4) != 0,
                CreatedAt = reader.GetString(5),
                Permissions = UserPermissions.Parse(reader.GetString(6), role),
            });
        }
        return list;
    }

    public static SystemUserRow? Get(int id) =>
        List(ativo: "todos").FirstOrDefault(u => u.Id == id);

    public static UserPermissions GetPermissions(int userId)
    {
        var u = Get(userId);
        return u?.Permissions ?? UserPermissions.ForRole("vendedor");
    }

    public static int Save(
        int? id,
        string login,
        string nome,
        string role,
        bool active,
        string? newPassword,
        UserPermissions? permissions = null)
    {
        ApplicationLoginService.EnsureLocalUserManagement();
        login = (login ?? "").Trim().ToLowerInvariant();
        nome = (nome ?? "").Trim();
        role = (role ?? "vendedor").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(login))
            throw new UsersException("Informe o login.");
        if (string.IsNullOrEmpty(nome))
            throw new UsersException("Informe o nome.");
        if (role is not ("admin" or "gestor" or "vendedor"))
            role = "vendedor";

        var perms = permissions ?? UserPermissions.ForRole(role);
        perms.Customized = permissions?.Customized ?? false;
        var permsJson = perms.ToJson();

        using var conn = DatabaseService.OpenConnection();

        if (id is > 0)
        {
            using var upd = conn.CreateCommand();
            if (!string.IsNullOrEmpty(newPassword))
            {
                if (newPassword.Length < 4)
                    throw new UsersException("Senha deve ter pelo menos 4 caracteres.");
                upd.CommandText = """
                    UPDATE users SET login=$login, nome=$nome, role=$role, active=$active,
                        password_hash=$hash, permissions_json=$perms WHERE id=$id;
                    """;
                upd.Parameters.AddWithValue("$hash", AuthService.HashPasswordCompatible(newPassword));
            }
            else
            {
                upd.CommandText = """
                    UPDATE users SET login=$login, nome=$nome, role=$role, active=$active,
                        permissions_json=$perms WHERE id=$id;
                    """;
            }
            upd.Parameters.AddWithValue("$id", id.Value);
            upd.Parameters.AddWithValue("$login", login);
            upd.Parameters.AddWithValue("$nome", nome);
            upd.Parameters.AddWithValue("$role", role);
            upd.Parameters.AddWithValue("$active", active ? 1 : 0);
            upd.Parameters.AddWithValue("$perms", permsJson);
            try
            {
                if (upd.ExecuteNonQuery() == 0)
                    throw new UsersException("Usuário não encontrado.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                throw new UsersException("Já existe um usuário com este login.");
            }

            AuditService.Log("alterar", "usuario", id.Value.ToString(), $"{login} / {role}");
            return id.Value;
        }

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
            throw new UsersException("Informe a senha (mín. 4 caracteres) para o novo usuário.");

        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO users (login, nome, password_hash, role, active, permissions_json)
            VALUES ($login, $nome, $hash, $role, $active, $perms);
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$login", login);
        ins.Parameters.AddWithValue("$nome", nome);
        ins.Parameters.AddWithValue("$hash", AuthService.HashPasswordCompatible(newPassword));
        ins.Parameters.AddWithValue("$role", role);
        ins.Parameters.AddWithValue("$active", active ? 1 : 0);
        ins.Parameters.AddWithValue("$perms", permsJson);
        try
        {
            var newId = Convert.ToInt32(ins.ExecuteScalar());
            AuditService.Log("criar", "usuario", newId.ToString(), $"{login} / {role}");
            return newId;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new UsersException("Já existe um usuário com este login.");
        }
    }

    public static void ResetPasswordByAdmin(int userId, string newPassword)
    {
        ApplicationLoginService.EnsureLocalPasswordChange();
        if (!AppSession.IsAdmin)
            throw new UsersException("Apenas administradores podem redefinir senha de outro usuário.");

        newPassword = newPassword ?? "";
        if (newPassword.Length < 4)
            throw new UsersException("Senha deve ter pelo menos 4 caracteres.");

        var user = Get(userId) ?? throw new UsersException("Usuário não encontrado.");
        if (SetupService.IsFactoryDefaultPassword(user.Login, newPassword))
            throw new UsersException("Escolha uma senha diferente da padrão de fábrica.");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET password_hash = $hash WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.Parameters.AddWithValue("$hash", AuthService.HashPasswordCompatible(newPassword));
        if (cmd.ExecuteNonQuery() == 0)
            throw new UsersException("Usuário não encontrado.");

        AuditService.Log("redefinir_senha", "usuario", userId.ToString(),
            $"Admin redefiniu senha de {user.Login}");
    }

    /// <summary>
    /// Auto-cadastro na tela de login: perfil vendedor, inativo até o admin aprovar.
    /// </summary>
    public static int RegisterSelf(string nome, string login, string? email, string password)
    {
        ApplicationLoginService.EnsureLocalUserAdministration();
        nome = (nome ?? "").Trim();
        login = (login ?? "").Trim().ToLowerInvariant();
        email = (email ?? "").Trim();
        password ??= "";

        if (string.IsNullOrEmpty(nome) || nome.Length < 2)
            throw new UsersException("Informe o nome completo.");
        if (string.IsNullOrEmpty(login) || login.Length < 2)
            throw new UsersException("Informe um usuário (mín. 2 caracteres).");
        if (!System.Text.RegularExpressions.Regex.IsMatch(login, @"^[a-z0-9._-]+$"))
            throw new UsersException("Usuário só pode ter letras, números, ponto, _ ou -.");
        if (!string.IsNullOrEmpty(email) && !email.Contains('@'))
            throw new UsersException("E-mail inválido.");
        if (password.Length < 4)
            throw new UsersException("Senha deve ter pelo menos 4 caracteres.");

        if (LoginExists(login))
            throw new UsersException("Este usuário já está em uso. Escolha outro.");

        var perms = UserPermissions.ForRole("vendedor");
        perms.Customized = false;

        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO users (login, nome, password_hash, role, active, permissions_json, email)
            VALUES ($login, $nome, $hash, 'vendedor', 0, $perms, $email);
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$login", login);
        ins.Parameters.AddWithValue("$nome", nome);
        ins.Parameters.AddWithValue("$hash", AuthService.HashPasswordCompatible(password));
        ins.Parameters.AddWithValue("$perms", perms.ToJson());
        ins.Parameters.AddWithValue("$email", string.IsNullOrEmpty(email) ? DBNull.Value : email);
        try
        {
            var newId = Convert.ToInt32(ins.ExecuteScalar());
            AuditService.Log("autocadastro", "usuario", newId.ToString(),
                $"{login} / vendedor (aguardando aprovação)");
            return newId;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new UsersException("Este usuário já está em uso. Escolha outro.");
        }
    }

    public static bool LoginExists(string login)
    {
        login = (login ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(login))
            return false;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM users WHERE lower(login) = $login LIMIT 1;";
        cmd.Parameters.AddWithValue("$login", login);
        return cmd.ExecuteScalar() is not null;
    }

    public static void Deactivate(int id)
    {
        ApplicationLoginService.EnsureLocalUserManagement();
        if (AppSession.CurrentUser?.Id == id)
            throw new UsersException("Você não pode desativar o próprio usuário logado.");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET active = 0 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new UsersException("Usuário não encontrado.");
        AuditService.Log("desativar", "usuario", id.ToString(), null);
    }

    public static (string Title, string Description) RolePermissionsDetail(string role) =>
        (role ?? "vendedor").Trim().ToLowerInvariant() switch
        {
            "admin" => (
                "Administrador — acesso total",
                "• PDV, descontos e cancelamento de vendas\n" +
                "• Cadastros, estoque, compras e financeiro\n" +
                "• Relatórios, backup, auditoria e usuários\n" +
                "• Ideal para o dono / TI do HL Bebidas"),
            "gestor" => (
                "Gestor — operação do depósito",
                "• PDV com desconto e cancelamento\n" +
                "• Clientes, produtos e ajuste de estoque\n" +
                "• Caixa, fiado, contas e relatórios\n" +
                "• Sem alterar usuários nem restaurar backup"),
            _ => (
                "Vendedor — balcão",
                "• PDV para registrar vendas\n" +
                "• Consulta clientes e produtos\n" +
                "• Caixa e receber fiado\n" +
                "• Sem contas a pagar, estorno ou exclusão de fiado"),
        };

    public static string RolePermissionsHint(string role) =>
        RolePermissionsDetail(role).Description.Replace("\n", " ");
}
