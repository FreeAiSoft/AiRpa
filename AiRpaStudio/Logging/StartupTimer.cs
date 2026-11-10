using System.Diagnostics;

namespace RPA.Logging
{
    /// <summary>
    /// Диагностика времени загрузки приложения. Пишет контрольные точки в log.txt
    /// (через <see cref="Log.Write"/>) с дельтой от предыдущей точки и суммарным временем
    /// от старта процесса. Не влияет на логику запуска — только замеры и логгирование.
    /// </summary>
    internal static class StartupTimer
    {
        private static readonly Stopwatch Total = Stopwatch.StartNew();
        private static long _lastMs;

        /// <summary>
        /// Логирует контрольную точку загрузки: сколько миллисекунд прошло от предыдущей
        /// точки и сколько всего от старта таймера.
        /// </summary>
        public static void Mark(string step)
        {
            long total = Total.ElapsedMilliseconds;
            long delta = total - _lastMs;
            _lastMs = total;
            Log.Write($"[Startup] {step}: +{delta} ms (total {total} ms)");
        }
    }
}
