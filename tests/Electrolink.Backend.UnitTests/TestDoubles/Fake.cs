using System.Collections;
using System.Reflection;

namespace Electrolink.Backend.UnitTests.TestDoubles;

/// <summary>One recorded call on a <see cref="Fake{T}"/>.</summary>
public sealed record Invocation(string Method, object?[] Args)
{
    public TArg Arg<TArg>(int index = 0) => (TArg)Args[index]!;
}

/// <summary>
///     Minimal interface test double built on <see cref="DispatchProxy"/> (BCL only, no mocking package).
///     Unconfigured members return safe defaults: completed tasks, empty collections, <c>false</c>/<c>0</c>/<c>null</c>.
///     Handlers return the plain result; the proxy wraps it into <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/>.
/// </summary>
public sealed class Fake<T> where T : class
{
    private readonly Dictionary<string, Func<object?[], object?>> _handlers = new(StringComparer.Ordinal);
    private readonly List<Invocation> _calls = [];

    public Fake()
    {
        Object = DispatchProxy.Create<T, FakeProxy>();
        ((FakeProxy)(object)Object).Owner = Dispatch;
    }

    public T Object { get; }

    public IReadOnlyList<Invocation> Calls => _calls;

    /// <summary>Configures every overload of <paramref name="method"/> to return the handler's result.</summary>
    public Fake<T> On(string method, Func<object?[], object?> handler)
    {
        _handlers[method] = handler;
        return this;
    }

    /// <summary>Configures every overload of <paramref name="method"/> to return a fixed value.</summary>
    public Fake<T> Returns(string method, object? value) => On(method, _ => value);

    /// <summary>Configures every overload of <paramref name="method"/> to throw.</summary>
    public Fake<T> Throws(string method, Exception exception) => On(method, _ => throw exception);

    public IReadOnlyList<Invocation> CallsTo(string method) => _calls.Where(c => c.Method == method).ToList();

    public int CallCount(string method) => _calls.Count(c => c.Method == method);

    private object? Dispatch(MethodInfo method, object?[] args)
    {
        var name = method.Name;
        if (name.StartsWith("get_", StringComparison.Ordinal) && _handlers.ContainsKey(name[4..])) name = name[4..];
        _calls.Add(new Invocation(name, args));
        var returnType = method.ReturnType;
        if (!_handlers.TryGetValue(name, out var handler)) return DefaultFor(returnType);
        object? result;
        try
        {
            result = handler(args);
        }
        catch (Exception e) when (IsAsync(returnType))
        {
            return Faulted(returnType, e);
        }
        return Wrap(returnType, result);
    }

    private static bool IsAsync(Type type) => typeof(Task).IsAssignableFrom(type) ||
        type == typeof(ValueTask) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    private static object Faulted(Type type, Exception e)
    {
        if (type == typeof(Task)) return Task.FromException(e);
        if (type == typeof(ValueTask)) return ValueTask.FromException(e);
        var inner = type.GetGenericArguments()[0];
        var task = typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!.MakeGenericMethod(inner).Invoke(null, [e])!;
        return type.GetGenericTypeDefinition() == typeof(ValueTask<>) ? Activator.CreateInstance(type, task)! : task;
    }

    private static object? Wrap(Type returnType, object? value)
    {
        if (returnType == typeof(void)) return null;
        if (value != null && returnType.IsInstanceOfType(value)) return value;
        if (returnType == typeof(Task)) return Task.CompletedTask;
        if (returnType == typeof(ValueTask)) return default(ValueTask);
        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            var inner = returnType.GetGenericArguments()[0];
            if (definition == typeof(Task<>)) return FromResult(inner, value);
            if (definition == typeof(ValueTask<>)) return Activator.CreateInstance(returnType, Coerce(inner, value));
        }
        return Coerce(returnType, value);
    }

    private static object FromResult(Type inner, object? value) =>
        typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [Coerce(inner, value)])!;

    private static object? Coerce(Type type, object? value)
    {
        if (value == null) return DefaultValue(type);
        if (type.IsInstanceOfType(value)) return value;
        // Allows handlers to return any IEnumerable for List<T>, arrays or read-only collection results.
        if (value is IEnumerable sequence && TryElementType(type, out var element))
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in sequence) list.Add(item);
            if (type.IsArray)
            {
                var array = Array.CreateInstance(element, list.Count);
                list.CopyTo(array, 0);
                return array;
            }
            if (type.IsInstanceOfType(list)) return list;
        }
        return value;
    }

    private static object? DefaultFor(Type returnType) => Wrap(returnType, null);

    private static object? DefaultValue(Type type)
    {
        if (type == typeof(void)) return null;
        if (type.IsValueType) return Activator.CreateInstance(type);
        if (type == typeof(string)) return null;
        if (TryElementType(type, out var element))
        {
            if (type.IsArray) return Array.CreateInstance(element, 0);
            var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            if (type.IsInstanceOfType(list)) return list;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() is var d &&
            (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>) || d == typeof(Dictionary<,>)))
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments()));
        return null;
    }

    private static bool TryElementType(Type type, out Type element)
    {
        if (type.IsArray)
        {
            element = type.GetElementType()!;
            return true;
        }
        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
        {
            var d = type.GetGenericTypeDefinition();
            if (d == typeof(IEnumerable<>) || d == typeof(IReadOnlyList<>) || d == typeof(IReadOnlyCollection<>) ||
                d == typeof(ICollection<>) || d == typeof(IList<>) || d == typeof(List<>))
            {
                element = type.GetGenericArguments()[0];
                return true;
            }
        }
        element = typeof(object);
        return false;
    }
}

/// <summary>Proxy type used by <see cref="Fake{T}"/>; must be public and non-sealed for DispatchProxy.</summary>
public class FakeProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[], object?> Owner { get; set; } = (_, _) => null;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Owner(targetMethod!, args ?? []);
}
