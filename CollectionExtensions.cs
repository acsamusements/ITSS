namespace ITSS
{
    public static class CollectionExtensions
    {
        public static bool IsNullOrEmpty<T>(this IEnumerable<T>? source)
            => source is null || !source.Any();

        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (T item in source)
                action(item);
        }

        // Filters out null entries from a sequence of nullable reference types
        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class
            => source.Where(x => x is not null)!;

        // Adds an item to the list only if it is not already present
        public static void AddIfNotExists<T>(this IList<T> list, T item)
        {
            if (!list.Contains(item))
                list.Add(item);
        }

        // Splits a list into chunks of a given size
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int size)
        {
            T[] bucket = [];
            int count = 0;

            foreach (T item in source)
            {
                if (count == size)
                {
                    yield return bucket;
                    bucket = [];
                    count = 0;
                }
                Array.Resize(ref bucket, count + 1);
                bucket[count++] = item;
            }

            if (count > 0)
                yield return bucket[..count];
        }

        // Returns the index of the first item matching the predicate, or -1 if not found
        public static int IndexOf<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            int index = 0;
            foreach (T item in source)
            {
                if (predicate(item)) return index;
                index++;
            }
            return -1;
        }
    }
}
