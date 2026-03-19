using System.Security.Cryptography;

namespace Atom.Web.Proxies.Services;

internal static class ProxyPoolSelection
{
    public static ServiceProxy? SelectSingle(
        IReadOnlyList<ServiceProxy> pool,
        Func<ServiceProxy, bool> filter,
        ProxyRotationStrategy strategy,
        ref int nextProxyIndex)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(filter);

        return strategy switch
        {
            ProxyRotationStrategy.Random => SelectSingleRandom(pool, filter),
            ProxyRotationStrategy.PreferFresh => SelectSinglePreferFresh(pool, filter),
            _ => SelectSingleRoundRobin(pool, filter, ref nextProxyIndex),
        };
    }

    public static IReadOnlyList<ServiceProxy> Select(
        IReadOnlyList<ServiceProxy> pool,
        int count,
        Func<ServiceProxy, bool> filter,
        ProxyRotationStrategy strategy,
        ref int nextProxyIndex)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(filter);

        return strategy switch
        {
            ProxyRotationStrategy.Random => SelectRandom(pool, count, filter),
            ProxyRotationStrategy.PreferFresh => SelectPreferFresh(pool, count, filter),
            _ => SelectRoundRobin(pool, count, filter, ref nextProxyIndex),
        };
    }

    private static ServiceProxy? SelectSingleRoundRobin(
        IReadOnlyList<ServiceProxy> pool,
        Func<ServiceProxy, bool> filter,
        ref int nextProxyIndex)
    {
        var filteredCount = CountFiltered(pool, filter);
        if (filteredCount == 0)
        {
            return null;
        }

        var targetIndex = Modulo(Interlocked.Increment(ref nextProxyIndex), filteredCount);
        var currentIndex = 0;
        for (var index = 0; index < pool.Count; index++)
        {
            var proxy = pool[index];
            if (!filter(proxy))
            {
                continue;
            }

            if (currentIndex == targetIndex)
            {
                return proxy;
            }

            currentIndex++;
        }

        return null;
    }

    private static ServiceProxy? SelectSingleRandom(IReadOnlyList<ServiceProxy> pool, Func<ServiceProxy, bool> filter)
    {
        var filteredCount = CountFiltered(pool, filter);
        if (filteredCount == 0)
        {
            return null;
        }

        var targetIndex = RandomNumberGenerator.GetInt32(filteredCount);
        var currentIndex = 0;
        for (var index = 0; index < pool.Count; index++)
        {
            var proxy = pool[index];
            if (!filter(proxy))
            {
                continue;
            }

            if (currentIndex == targetIndex)
            {
                return proxy;
            }

            currentIndex++;
        }

        return null;
    }

    private static ServiceProxy? SelectSinglePreferFresh(IReadOnlyList<ServiceProxy> pool, Func<ServiceProxy, bool> filter)
    {
        ServiceProxy? selected = null;
        for (var index = 0; index < pool.Count; index++)
        {
            var proxy = pool[index];
            if (!filter(proxy))
            {
                continue;
            }

            if (selected is null || CompareFreshness(proxy, selected) < 0)
            {
                selected = proxy;
            }
        }

        return selected;
    }

    private static IReadOnlyList<ServiceProxy> SelectRoundRobin(
        IReadOnlyList<ServiceProxy> pool,
        int count,
        Func<ServiceProxy, bool> filter,
        ref int nextProxyIndex)
    {
        var filteredCount = CountFiltered(pool, filter);
        if (filteredCount == 0)
        {
            return [];
        }

        var actualCount = Math.Min(count, filteredCount);
        var endIndex = Interlocked.Add(ref nextProxyIndex, actualCount);
        var startIndex = endIndex - actualCount + 1;
        var targetStart = Modulo(startIndex, filteredCount);

        var result = new ServiceProxy[actualCount];
        var resultIndex = FillRoundRobinSelection(pool, filter, targetStart, filteredCount, result);
        if (resultIndex < actualCount)
        {
            FillRoundRobinSelection(pool, filter, startFilteredIndex: 0, endFilteredIndex: targetStart, result, resultIndex);
        }

        return result;
    }

    private static IReadOnlyList<ServiceProxy> SelectRandom(
        IReadOnlyList<ServiceProxy> pool,
        int count,
        Func<ServiceProxy, bool> filter)
    {
        if (count == 1)
        {
            var selected = SelectSingleRandom(pool, filter);
            return selected is null ? [] : [selected];
        }

        var reservoir = new ServiceProxy[count];
        var filteredCount = 0;

        for (var index = 0; index < pool.Count; index++)
        {
            var proxy = pool[index];
            if (!filter(proxy))
            {
                continue;
            }

            if (filteredCount < reservoir.Length)
            {
                reservoir[filteredCount++] = proxy;
                continue;
            }

            var swapIndex = RandomNumberGenerator.GetInt32(filteredCount + 1);
            if (swapIndex < reservoir.Length)
            {
                reservoir[swapIndex] = proxy;
            }

            filteredCount++;
        }

        if (filteredCount == 0)
        {
            return [];
        }

        var actualCount = Math.Min(count, filteredCount);
        ShuffleSelected(reservoir, actualCount);

        var result = new ServiceProxy[actualCount];
        for (var index = 0; index < actualCount; index++)
        {
            result[index] = reservoir[index];
        }

        return result;
    }

    private static IReadOnlyList<ServiceProxy> SelectPreferFresh(
        IReadOnlyList<ServiceProxy> pool,
        int count,
        Func<ServiceProxy, bool> filter)
    {
        if (count == 1)
        {
            var selected = SelectSinglePreferFresh(pool, filter);
            return selected is null ? [] : [selected];
        }

        var result = new ServiceProxy[count];
        var resultCount = 0;

        for (var index = 0; index < pool.Count; index++)
        {
            var proxy = pool[index];
            if (!filter(proxy))
            {
                continue;
            }

            InsertPreferFresh(result, ref resultCount, proxy);
        }

        if (resultCount == 0)
        {
            return [];
        }

        if (resultCount == result.Length)
        {
            return result;
        }

        var trimmed = new ServiceProxy[resultCount];
        Array.Copy(result, trimmed, resultCount);
        return trimmed;
    }

    private static List<ServiceProxy> MaterializeFiltered(IReadOnlyList<ServiceProxy> pool, Func<ServiceProxy, bool> filter)
    {
        var filtered = new List<ServiceProxy>(pool.Count);
        for (var index = 0; index < pool.Count; index++)
        {
            var proxy = pool[index];
            if (filter(proxy))
            {
                filtered.Add(proxy);
            }
        }

        return filtered;
    }

    private static int FillRoundRobinSelection(
        IReadOnlyList<ServiceProxy> pool,
        Func<ServiceProxy, bool> filter,
        int startFilteredIndex,
        int endFilteredIndex,
        ServiceProxy[] result,
        int resultIndex = 0)
    {
        if (startFilteredIndex >= endFilteredIndex)
        {
            return resultIndex;
        }

        var filteredIndex = 0;
        for (var index = 0; index < pool.Count && resultIndex < result.Length; index++)
        {
            var proxy = pool[index];
            if (!filter(proxy))
            {
                continue;
            }

            if (filteredIndex >= startFilteredIndex && filteredIndex < endFilteredIndex)
            {
                result[resultIndex++] = proxy;
            }

            filteredIndex++;
        }

        return resultIndex;
    }

    private static int CountFiltered(IReadOnlyList<ServiceProxy> pool, Func<ServiceProxy, bool> filter)
    {
        var count = 0;
        for (var index = 0; index < pool.Count; index++)
        {
            if (filter(pool[index]))
            {
                count++;
            }
        }

        return count;
    }

    private static void InsertPreferFresh(ServiceProxy[] result, ref int resultCount, ServiceProxy proxy)
    {
        var insertIndex = 0;
        while (insertIndex < resultCount && CompareFreshness(proxy, result[insertIndex]) >= 0)
        {
            insertIndex++;
        }

        if (insertIndex >= result.Length)
        {
            return;
        }

        if (resultCount < result.Length)
        {
            resultCount++;
        }

        for (var index = resultCount - 1; index > insertIndex; index--)
        {
            result[index] = result[index - 1];
        }

        result[insertIndex] = proxy;
    }

    private static void ShuffleSelected(ServiceProxy[] selected, int count)
    {
        for (var index = count - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (selected[index], selected[swapIndex]) = (selected[swapIndex], selected[index]);
        }
    }

    private static int CompareFreshness(ServiceProxy left, ServiceProxy right)
    {
        var aliveComparison = right.Alive.CompareTo(left.Alive);
        if (aliveComparison != 0)
        {
            return aliveComparison;
        }

        return right.Uptime.CompareTo(left.Uptime);
    }

    private static int Modulo(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}