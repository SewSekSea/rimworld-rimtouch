// Independent two-contact twist classification. GPL-3.0-or-later.
using System;

namespace RimTouch.TouchPlatform
{
    public sealed class TwistGesture
    {
        private bool tracking, committed;
        private float previousAngle, pending;
        public bool HasTwisted { get; private set; }
        public void Reset()
        {
            tracking = committed = HasTwisted = false;
            previousAngle = pending = 0;
        }
        public float Update(float ax, float ay, float bx, float by)
        {
            float dx = bx-ax, dy = by-ay;
            if (float.IsNaN(dx) || float.IsInfinity(dx) || float.IsNaN(dy) || float.IsInfinity(dy) || dx*dx+dy*dy < 32f*32f)
            { tracking = committed = false; pending = 0; return 0; }
            float angle = (float)(Math.Atan2(dy,dx)*180/Math.PI);
            if (!tracking) { tracking = true; previousAngle = angle; return 0; }
            float delta = angle-previousAngle;
            previousAngle = angle;
            if (delta > 180) delta -= 360;
            if (delta < -180) delta += 360;
            // A contact crossing or sample discontinuity should not flip the globe.
            if (Math.Abs(delta) > 90) { committed = false; pending = 0; return 0; }
            if (committed) return delta;
            pending += delta;
            if (Math.Abs(pending) < 4) return 0;
            committed = HasTwisted = true;
            float result = pending;
            pending = 0;
            return result;
        }
    }
}
