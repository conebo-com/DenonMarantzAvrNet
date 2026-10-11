using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace DenonMarantzAvrNet
{
    internal static class NetFrameworkCompat
    {
        private const int CopyBufferSize = 81920;
        private const int FileBufferSize = 4096;

        public static async Task<byte[]> ReadAsByteArrayAsync(this HttpContent content, CancellationToken cancellationToken)
        {
            using Stream sourceStream = await content.ReadAsStreamAsync();
            using MemoryStream memoryStream = new MemoryStream();

            await sourceStream.CopyToAsync(memoryStream, CopyBufferSize, cancellationToken);

            return memoryStream.ToArray();
            // <-----------
        }

        public static async Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken cancellationToken)
        {
            byte[] contentBytes = await content.ReadAsByteArrayAsync(cancellationToken);
            Encoding encoding = Encoding.UTF8;
            string? charSet = content.Headers.ContentType?.CharSet;

            if (!string.IsNullOrWhiteSpace(charSet))
            {
                try
                {
                    encoding = Encoding.GetEncoding(charSet!.Trim('"'));
                }
                catch (ArgumentException)
                {
                    encoding = Encoding.UTF8;
                }
            }

            using StreamReader reader = new StreamReader(new MemoryStream(contentBytes), encoding, true);

            return reader.ReadToEnd();
            // <-----------
        }

        public static async Task WriteAllBytesAsync(string path, byte[] bytes)
        {
            using FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, FileBufferSize, true);

            await fileStream.WriteAsync(bytes, 0, bytes.Length);
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct Int32SingleUnion
        {
            [FieldOffset(0)]
            public int IntValue;

            [FieldOffset(0)]
            public float SingleValue;
        }

        public static float ReadSingleBigEndian(ReadOnlySpan<byte> source)
        {
            Int32SingleUnion union = new Int32SingleUnion
            {
                IntValue = (source[0] << 24)
                    | (source[1] << 16)
                    | (source[2] << 8)
                    | source[3]
            };

            return union.SingleValue;
            // <-----------
        }

        public static class BitOperations
        {
            public static int PopCount(ulong value)
            {
                int count = 0;

                while (value != 0)
                {
                    value &= value - 1;
                    count++;
                }

                return count;
                // <-----------
            }
        }

        public static void ThrowIfNullOrWhiteSpace(string? argument, string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
                // <-----------
            }

            if (string.IsNullOrWhiteSpace(argument))
            {
                throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", paramName);
                // <-----------
            }
        }

        public static void ThrowIfNull(object? argument, string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
                // <-----------
            }
        }

        public static void ThrowIfDisposed(bool condition, object? instance)
        {
            if (condition)
            {
                throw new ObjectDisposedException(instance?.GetType().FullName);
                // <-----------
            }
        }
    }
}