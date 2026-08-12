using System.Text.Json;

namespace SGDB.Models;

public sealed class PersonRoles
{
    public bool Ativo { get; set; } = true;
    public bool Clientes { get; set; }
    public bool Fornecedores { get; set; }
    public bool Funcionarios { get; set; }
    public bool Credenciadoras { get; set; }
    public bool Parceiros { get; set; }
    public bool CcfSpc { get; set; }
    public bool Estrangeiro { get; set; }
    public bool Marketplaces { get; set; }

    public static PersonRoles ForNewCliente() => new() { Ativo = true, Clientes = true };

    public static PersonRoles Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new PersonRoles();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new PersonRoles
            {
                Ativo = ReadBool(root, "ativo", true),
                Clientes = ReadBool(root, "clientes"),
                Fornecedores = ReadBool(root, "fornecedores"),
                Funcionarios = ReadBool(root, "funcionarios"),
                Credenciadoras = ReadBool(root, "credenciadoras"),
                Parceiros = ReadBool(root, "parceiros"),
                CcfSpc = ReadBool(root, "ccf_spc"),
                Estrangeiro = ReadBool(root, "estrangeiro"),
                Marketplaces = ReadBool(root, "marketplaces"),
            };
        }
        catch
        {
            return new PersonRoles();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, bool>
    {
        ["ativo"] = Ativo,
        ["clientes"] = Clientes,
        ["fornecedores"] = Fornecedores,
        ["funcionarios"] = Funcionarios,
        ["credenciadoras"] = Credenciadoras,
        ["parceiros"] = Parceiros,
        ["ccf_spc"] = CcfSpc,
        ["estrangeiro"] = Estrangeiro,
        ["marketplaces"] = Marketplaces,
    });

    private static bool ReadBool(JsonElement root, string name, bool defaultValue = false)
    {
        if (!root.TryGetProperty(name, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.GetInt32() != 0,
            JsonValueKind.String => el.GetString() is "1" or "true" or "True",
            _ => defaultValue,
        };
    }
}
