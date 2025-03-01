using System.Text;
using Serilog.Debugging;

namespace Serilog.Sinks.AzureDataExplorer.Durable
{
    /// <summary>
    /// Abstract payload reader
    /// Generic version of https://github.com/serilog/serilog-sinks-seq/blob/v4.0.0/src/Serilog.Sinks.Seq/Sinks/Seq/Durable/PayloadReader.cs
    /// </summary>
    /// <typeparam name="TPayload"></typeparam>
    public abstract class APayloadReader<TPayload> : IPayloadReader<TPayload>
    {
        /// <summary>
        /// Abstract method whose implementation returns empty payload
        /// </summary>
        /// <returns></returns>
        public abstract TPayload GetNoPayload();

        /// <summary>
        /// reads data from a file and process it in batches
        /// </summary>
        /// <param name="batchPostingLimit"></param>
        /// <param name="eventBodyLimitBytes"></param>
        /// <param name="position"></param>
        /// <param name="count"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public TPayload ReadPayload(int batchPostingLimit, long? eventBodyLimitBytes, ref FileSetPosition position, ref int count, string fileName)
        {
            InitPayLoad(fileName);

            using (var current = System.IO.File.Open(position.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var nextLineStart = position.NextLineStart;
                while (count < batchPostingLimit && TryReadLine(current, ref nextLineStart, out var nextLine))
                {
                    position = new FileSetPosition(nextLineStart, position.File);

                    // Count is the indicator that work was done, so advances even in the (rare) case an
                    // oversized event is dropped.
                    ++count;

                    if (eventBodyLimitBytes.HasValue && Encoding.UTF8.GetByteCount(nextLine) > eventBodyLimitBytes.Value)
                    {
                        SelfLog.WriteLine(
                            "Event JSON representation exceeds the byte size limit of {0} and will be dropped; data: {1}",
                            eventBodyLimitBytes, nextLine);
                    }
                    else
                    {
                        AddToPayLoad(nextLine);
                    }
                }
            }
            return FinishPayLoad();
        }

        /// <summary>
        /// abstract method for InitPayload
        /// </summary>
        /// <param name="fileName"></param>
        protected abstract void InitPayLoad(string fileName);

        /// <summary>
        /// Abstract method of finish payload
        /// </summary>
        /// <returns></returns>
        protected abstract TPayload FinishPayLoad();

        /// <summary>
        /// abstract method for AddToPayload
        /// </summary>
        /// <param name="nextLine"></param>
        protected abstract void AddToPayLoad(string nextLine);

        // It would be ideal to chomp whitespace here, but not required.
        private static bool TryReadLine(Stream current, ref long nextStart, out string nextLine)
        {
            var includesBom = nextStart == 0;

            if (current.Length <= nextStart)
            {
                nextLine = null;
                return false;
            }

            current.Position = nextStart;

            // Important not to dispose this StreamReader as the stream must remain open.
            var reader = new StreamReader(current, Encoding.UTF8, false, 128);
            nextLine = reader.ReadLine();

            if (nextLine == null)
                return false;

            nextStart += Encoding.UTF8.GetByteCount(nextLine) + Encoding.UTF8.GetByteCount(Environment.NewLine);
            if (includesBom)
                nextStart += 3;

            return true;
        }
    }
}
