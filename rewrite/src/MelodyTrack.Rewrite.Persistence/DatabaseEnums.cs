using System.Data;
using Dapper;
using MelodyTrack.Rewrite.Domain;
using Npgsql;
using Npgsql.NameTranslation;

namespace MelodyTrack.Rewrite.Persistence;

/// <summary>
/// Настраивает соответствия предметных перечислений нативным типам PostgreSQL.
/// </summary>
public static class DatabaseEnums
{
    private const string ContactFormatTypeName = "public.contact_format";

    /// <summary>
    /// Добавляет отображение ContactFormat на contact_format с метками phone, email и link.
    /// </summary>
    /// <param name="builder">Builder источника данных до вызова Build.</param>
    /// <returns>Тот же builder для дальнейшей настройки.</returns>
    /// <remarks>
    /// Типы PostgreSQL должны быть созданы Init до первого подключения этого источника данных.
    /// Метод не открывает соединений и не изменяет глобальные настройки Npgsql или Dapper.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Builder не передан.</exception>
    public static NpgsqlDataSourceBuilder MapDomainEnums(this NpgsqlDataSourceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.MapEnum<ContactFormat>(ContactFormatTypeName, new NpgsqlSnakeCaseNameTranslator());
    }

    internal static object? ParameterValue(object? value, DbType? type)
    {
        if (value is not ContactFormat format) return value;
        if (type is not null)
            throw new ArgumentException("Для PostgreSQL enum тип определяется зарегистрированным отображением, а не DbType.", nameof(type));
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(value), "Неизвестный формат контакта.");
        return new ContactFormatParameter(format);
    }

    private sealed class ContactFormatParameter(ContactFormat value) : SqlMapper.ICustomQueryParameter
    {
        public void AddParameter(IDbCommand command, string name)
        {
            if (command is not NpgsqlCommand postgres)
                throw new InvalidOperationException("Параметр PostgreSQL enum требует NpgsqlCommand.");

            postgres.Parameters.Add(new NpgsqlParameter<ContactFormat>
            {
                ParameterName = name,
                TypedValue = value,
                DataTypeName = ContactFormatTypeName
            });
        }
    }
}
