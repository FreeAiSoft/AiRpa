using System;

namespace ActivityLibraryBase.Operations
{
    public class Timing : ICloneable
    {
        public TimeSpan DelayAfter { get; set; }

        public TimeSpan DelayBefore { get; set; }

        public TimeSpan Timeout { get; set; }

        public Timing()
        {
            Timeout = TimeSpan.FromSeconds(30);
            DelayBefore = TimeSpan.FromSeconds(1);
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        public Timing Clone()
        {
            return new Timing
            {
                DelayAfter = DelayAfter,
                DelayBefore = DelayBefore,
                Timeout = Timeout
            };
        }
    }
}
