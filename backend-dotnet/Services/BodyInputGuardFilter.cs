using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Textzy.Api.Services;

public class BodyInputGuardFilter : IActionFilter
{
    private const int MaxStringLength = 20000;
    private const int MaxDepth = 8;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var bodyParamNames = context.ActionDescriptor.Parameters
            .OfType<ControllerParameterDescriptor>()
            .Where(p => p.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (bodyParamNames.Count == 0) return;

        foreach (var arg in context.ActionArguments)
        {
            if (!bodyParamNames.Contains(arg.Key)) continue;
            try
            {
                ValidateObjectGraph(arg.Value, arg.Key, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }
            catch (InvalidOperationException ex)
            {
                context.Result = new BadRequestObjectResult(ex.Message);
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private static void ValidateObjectGraph(object? value, string path, int depth, HashSet<object> visited)
    {
        if (value is null) return;
        if (depth > MaxDepth) return;

        if (value is string text)
        {
            ValidateString(text, path);
            return;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan))
        {
            return;
        }

        if (!type.IsValueType)
        {
            if (!visited.Add(value)) return;
        }

        if (value is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                var key = entry.Key?.ToString() ?? "key";
                ValidateObjectGraph(entry.Value, $"{path}.{key}", depth + 1, visited);
            }
            return;
        }

        if (value is IEnumerable enumerable)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                ValidateObjectGraph(item, $"{path}[{index}]", depth + 1, visited);
                index++;
            }
            return;
        }

        var props = type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        foreach (var prop in props)
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            object? propValue;
            try
            {
                propValue = prop.GetValue(value);
            }
            catch
            {
                continue;
            }

            ValidateObjectGraph(propValue, $"{path}.{prop.Name}", depth + 1, visited);
        }
    }

    private static void ValidateString(string value, string path)
    {
        if (value.Length > MaxStringLength)
            throw new InvalidOperationException($"{path} exceeds maximum length.");

        foreach (var ch in value)
        {
            if (ch == '\0')
                throw new InvalidOperationException($"{path} contains invalid characters.");
            if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t')
                throw new InvalidOperationException($"{path} contains invalid control characters.");
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
