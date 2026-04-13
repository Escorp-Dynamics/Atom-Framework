using System.Reflection;
using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

internal static class PublicApiAssert
{
    internal static EventInfo RequireEvent(Type type, string name)
    {
        var eventInfo = type.GetEvent(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        Assert.That(eventInfo, Is.Not.Null, $"У типа '{type.Name}' ожидалось событие '{name}'.");
        return eventInfo!;
    }

    internal static PropertyInfo RequireProperty(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        Assert.That(property, Is.Not.Null, $"У типа '{type.Name}' ожидалось свойство '{name}'.");
        return property!;
    }

    internal static MethodInfo RequireMethod(Type type, string name, params string[] parameterTypeTokens)
    {
        var method = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal)
                && ParametersMatch(candidate.GetParameters(), parameterTypeTokens));

        Assert.That(method, Is.Not.Null,
            $"У типа '{type.Name}' ожидался метод '{name}' с параметрами [{string.Join(", ", parameterTypeTokens)}].");
        return method!;
    }

    internal static MethodInfo RequireGenericMethod(Type type, string name, params string[] parameterTypeTokens)
    {
        var method = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(candidate =>
                candidate.IsGenericMethodDefinition
                && string.Equals(candidate.Name, name, StringComparison.Ordinal)
                && ParametersMatch(candidate.GetParameters(), parameterTypeTokens));

        Assert.That(method, Is.Not.Null,
            $"У типа '{type.Name}' ожидался generic-метод '{name}' с параметрами [{string.Join(", ", parameterTypeTokens)}].");
        return method!;
    }

    internal static void AssertReturnTypeContains(MethodInfo method, string token)
        => Assert.That(method.ReturnType.ToString(), Does.Contain(token),
            $"Метод '{method.DeclaringType?.Name}.{method.Name}' должен возвращать тип, содержащий '{token}'.");

    private static bool ParametersMatch(ParameterInfo[] parameters, string[] tokens)
    {
        if (parameters.Length != tokens.Length)
            return false;

        for (var index = 0; index < parameters.Length; index++)
        {
            if (!TypeMatches(parameters[index].ParameterType, tokens[index]))
                return false;
        }

        return true;
    }

    private static bool TypeMatches(Type type, string token)
    {
        if (string.Equals(type.Name, token, StringComparison.Ordinal)
            || string.Equals(type.FullName, token, StringComparison.Ordinal)
            || type.ToString().Contains(token, StringComparison.Ordinal))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            var genericDefinition = type.GetGenericTypeDefinition();
            if (genericDefinition.Name.Contains(token, StringComparison.Ordinal)
                || genericDefinition.FullName?.Contains(token, StringComparison.Ordinal) == true)
            {
                return true;
            }
        }

        return false;
    }
}