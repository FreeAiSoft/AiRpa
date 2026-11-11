using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RPA.Logging
{
    /// <summary>
    /// Диагностика времени загрузки приложения. Пишет контрольные точки в log.txt
    /// (через <see cref="Log.Write"/>) с дельтой от предыдущей точки и суммарным временем
    /// от старта процесса. Не влияет на логику запуска — только замеры и логгирование.
    /// <para>
    /// Каждая метка дополнительно уходит в панель «Log» через подключаемый приёмник
    /// (<see cref="Attach"/>). Панель создаётся позже первых меток старта, поэтому до подключения
    /// строки накапливаются в буфере и сливаются в приёмник при подключении.
    /// </para>
    /// </summary>
    internal static class StartupTimer
    {
        private static readonly Stopwatch Total = Stopwatch.StartNew();
        private static long _lastMs;

        private static readonly object Gate = new object();
        private static readonly List<string> Pending = new List<string>();
        private static Action<string> _sink;

        /// <summary>
        /// Логирует контрольную точку загрузки: сколько миллисекунд прошло от предыдущей
        /// точки и сколько всего от старта таймера.
        /// </summary>
        public static void Mark(string step)
        {
            long total = Total.ElapsedMilliseconds;
            long delta = total - _lastMs;
            _lastMs = total;
            string line = $"[Startup] {step}: +{delta} ms (total {total} ms)";

            Log.Write(line);
            Forward(line);
        }

        /// <summary>
        /// Подключает приёмник строки (панель «Log») и сливает в него метки, накопленные до
        /// подключения, в порядке поступления.
        /// </summary>
        public static void Attach(Action<string> sink)
        {
            if (sink == null)
            {
                return;
            }

            List<string> buffered;
            lock (Gate)
            {
                _sink = sink;
                buffered = new List<string>(Pending);
                Pending.Clear();
            }

            foreach (string line in buffered)
            {
                sink(line);
            }
        }

        /// <summary>Отключает приёмник и очищает буфер (тестовый шов).</summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                _sink = null;
                Pending.Clear();
            }
        }

        private static void Forward(string line)
        {
            Action<string> sink;
            lock (Gate)
            {
                if (_sink == null)
                {
                    Pending.Add(line);
                    return;
                }

                sink = _sink;
            }

            sink(line);
        }
    }
}
