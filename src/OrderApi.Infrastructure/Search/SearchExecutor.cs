using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Exceptions;

namespace OrderApi.Infrastructure.Search;

/// <summary>
/// Shared filter/sort/pagination engine. Fields are resolved from the EF Core model metadata
/// (never from a hand-maintained allowlist), accepting both the CLR name and snake_case.
/// [Sensitive] only governs what is serialized and logged, not what can be searched: finding a customer by tax id or
/// e-mail is a legitimate lookup, and every authenticated caller already gets the same rows either way.
/// </summary>
public static class SearchExecutor
{
    private const string IdProperty = "Id";

    private static readonly MethodInfo EfProperty = typeof(EF).GetMethod(nameof(EF.Property))!;

    public static async Task<PagedResult<T>> SearchAsync<T>(
        this IQueryable<T> source,
        SearchRequest request,
        IEntityType entityType,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var filtered = ApplyFilters(source, request.Filters, entityType);
        var total = await filtered.LongCountAsync(cancellationToken);
        var items = await ApplySort(filtered, request.Sort, entityType)
            .Skip(request.Page * request.Size)
            .Take(request.Size)
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(items, total, request.Page, request.Size);
    }

    private static IQueryable<T> ApplyFilters<T>(
        IQueryable<T> source,
        IReadOnlyDictionary<string, IReadOnlyList<FilterCondition>>? filters,
        IEntityType entityType)
    {
        if (filters is null)
        {
            return source;
        }

        var parameter = Expression.Parameter(typeof(T), "e");
        foreach (var (field, conditions) in filters)
        {
            var property = ResolveProperty(entityType, field);
            if (property is null)
            {
                continue; // unknown filter field: treated as if the client had not sent it
            }

            foreach (var condition in conditions)
            {
                var predicate = BuildPredicate(parameter, property, field, condition);
                source = source.Where(Expression.Lambda<Func<T, bool>>(predicate, parameter));
            }
        }

        return source;
    }

    private static IQueryable<T> ApplySort<T>(IQueryable<T> source, string? sort, IEntityType entityType)
    {
        var orderings = new List<(IProperty Property, bool Descending)>();
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var field = sort.TrimStart('-');
            var property = ResolveProperty(entityType, field)
                ?? throw new ValidationDomainException(
                    $"Unknown sort field: {field}",
                    ErrorCodes.ValidationUnknownSort,
                    new Dictionary<string, object?> { ["field"] = field });
            orderings.Add((property, sort.StartsWith('-')));
        }

        // Stable tie-breaker so pages stay consistent when the sort field has repeated values.
        var id = entityType.FindProperty(IdProperty)!;
        if (orderings.All(o => o.Property != id))
        {
            orderings.Add((id, false));
        }

        var parameter = Expression.Parameter(typeof(T), "e");
        var first = true;
        foreach (var (property, descending) in orderings)
        {
            var key = PropertyAccess(parameter, property);
            var methodName = (first, descending) switch
            {
                (true, false) => nameof(Queryable.OrderBy),
                (true, true) => nameof(Queryable.OrderByDescending),
                (false, false) => nameof(Queryable.ThenBy),
                (false, true) => nameof(Queryable.ThenByDescending),
            };
            var method = typeof(Queryable).GetMethods()
                .First(m => m.Name == methodName && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(T), property.ClrType);
            source = (IQueryable<T>)method.Invoke(null, [source, Expression.Lambda(key, parameter)])!;
            first = false;
        }

        return source;
    }

    private static IProperty? ResolveProperty(IEntityType entityType, string field)
    {
        var normalized = field.ToLowerInvariant();
        return entityType.GetProperties().FirstOrDefault(p =>
            !p.IsShadowProperty()
            && (string.Equals(p.Name, field, StringComparison.OrdinalIgnoreCase) || ToSnakeCase(p.Name) == normalized));
    }

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static MethodCallExpression PropertyAccess(ParameterExpression parameter, IProperty property) =>
        Expression.Call(EfProperty.MakeGenericMethod(property.ClrType), parameter, Expression.Constant(property.Name));

    private static Expression BuildPredicate(ParameterExpression parameter, IProperty property, string field, FilterCondition condition)
    {
        var member = PropertyAccess(parameter, property);
        try
        {
            var op = condition.Op.ToLowerInvariant();
            if (op is "gt" or "gte" or "lt" or "lte" or "between")
            {
                RequireOrderable(property);
            }

            return op switch
            {
                "eq" => Expression.Equal(member, Constant(condition.Value, property)),
                "neq" => Expression.NotEqual(member, Constant(condition.Value, property)),
                "gt" => Ordered(member, property, condition.Value, Expression.GreaterThan),
                "gte" => Ordered(member, property, condition.Value, Expression.GreaterThanOrEqual),
                "lt" => Ordered(member, property, condition.Value, Expression.LessThan),
                "lte" => Ordered(member, property, condition.Value, Expression.LessThanOrEqual),
                "in" => BuildIn(member, property, condition.Value),
                "between" => BuildBetween(member, property, condition.Value),
                "lk" => BuildLike(member, property, condition.Value),
                _ => throw new FormatException($"Unknown operator: {condition.Op}"),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            throw new ValidationDomainException(
                $"Invalid value for field: {field}",
                ErrorCodes.ValidationInvalidFilter,
                new Dictionary<string, object?> { ["field"] = field });
        }
    }

    // Enum and bool columns have no meaningful order (an enum is stored as its text code, so "greater than"
    // would compare alphabetically): only equality and membership make sense for them.
    private static void RequireOrderable(IProperty property)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (type.IsEnum || type == typeof(bool))
        {
            throw new FormatException("Ordering operators are not supported for this field type");
        }
    }

    // Expression trees have no ">=" for strings (it would throw InvalidOperationException, i.e. a 400 for every
    // string field); string.Compare(a, b) op 0 is the form EF Core translates.
    private static BinaryExpression Ordered(
        Expression member, IProperty property, object? raw, Func<Expression, Expression, BinaryExpression> compare)
    {
        var constant = Constant(raw, property);
        if (property.ClrType != typeof(string))
        {
            return compare(member, constant);
        }

        var comparison = Expression.Call(typeof(string), nameof(string.Compare), null, member, constant);
        return compare(comparison, Expression.Constant(0));
    }

    private static MethodCallExpression BuildIn(Expression member, IProperty property, object? raw)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(property.ClrType))!;
        foreach (var element in Elements(raw))
        {
            list.Add(ConvertValue(element, property.ClrType));
        }

        return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [property.ClrType], Expression.Constant(list), member);
    }

    private static BinaryExpression BuildBetween(Expression member, IProperty property, object? raw)
    {
        var bounds = Elements(raw).ToList();
        if (bounds.Count != 2)
        {
            throw new FormatException("between requires exactly two values");
        }

        return Expression.AndAlso(
            Ordered(member, property, bounds[0], Expression.GreaterThanOrEqual),
            Ordered(member, property, bounds[1], Expression.LessThanOrEqual));
    }

    private static MethodCallExpression BuildLike(Expression member, IProperty property, object? raw)
    {
        if (property.ClrType != typeof(string))
        {
            throw new FormatException("lk is only supported for string fields");
        }

        var term = ConvertValue(raw, typeof(string)) as string ?? throw new FormatException("lk requires a value");
        var escaped = term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return Expression.Call(
            typeof(NpgsqlDbFunctionsExtensions),
            nameof(NpgsqlDbFunctionsExtensions.ILike),
            null,
            Expression.Constant(EF.Functions),
            member,
            Expression.Constant($"%{escaped}%"));
    }

    private static ConstantExpression Constant(object? raw, IProperty property) =>
        Expression.Constant(ConvertValue(raw, property.ClrType), property.ClrType);

    private static IEnumerable<object?> Elements(object? raw) => raw switch
    {
        JsonElement { ValueKind: JsonValueKind.Array } array => array.EnumerateArray().Select(e => (object?)e),
        string text => [text],
        IEnumerable enumerable => enumerable.Cast<object?>(),
        _ => throw new FormatException("A list of values is required"),
    };

    // yyyy-MM-dd... only: DateTimeOffset.TryParse alone also guesses "01/02/2025" and "Jan 2 2025".
    private static bool LooksLikeIsoDate(string text) =>
        text.Length >= 10 && text[4] == '-' && text[7] == '-' && text[..4].All(char.IsAsciiDigit);

    private static object? ConvertValue(object? raw, Type clrType)
    {
        var nullable = Nullable.GetUnderlyingType(clrType);
        var type = nullable ?? clrType;
        var text = raw switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement element => element.GetRawText(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString(),
        };

        if (text is null)
        {
            return nullable is not null || !type.IsValueType ? null : throw new FormatException("A value is required");
        }

        var culture = CultureInfo.InvariantCulture;
        if (type == typeof(string))
        {
            return text;
        }

        if (type == typeof(Guid) && Guid.TryParse(text, out var guid))
        {
            return guid;
        }

        if (type == typeof(bool) && bool.TryParse(text, out var boolean))
        {
            return boolean;
        }

        if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, culture, out var integer))
        {
            return integer;
        }

        if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer, culture, out var integer64))
        {
            return integer64;
        }

        if (type == typeof(uint) && uint.TryParse(text, NumberStyles.Integer, culture, out var unsigned))
        {
            return unsigned;
        }

        if (type == typeof(decimal) && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, culture, out var number))
        {
            return number;
        }

        if (type == typeof(DateTimeOffset) && LooksLikeIsoDate(text) && DateTimeOffset.TryParse(text, culture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return date;
        }

        // Names only: Enum.TryParse would also accept "1" or "99" (an undefined value the converter cannot map).
        if (type.IsEnum && char.IsAsciiLetter(text[0]) && Enum.TryParse(type, text, true, out var enumValue)
            && Enum.IsDefined(type, enumValue))
        {
            return enumValue;
        }

        throw new FormatException($"Cannot convert '{text}' to {type.Name}");
    }
}
