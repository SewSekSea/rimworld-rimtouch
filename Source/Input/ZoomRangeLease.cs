// Conditional camera-range ownership. GPL-3.0-or-later.
namespace RimTouch.TouchPlatform
{
    public sealed class ZoomRangeLease
    {
        private bool active;
        private float originalMin, originalMax, appliedMin, appliedMax;

        public bool Update(float currentMin, float currentMax, bool enabled, float requestedMin, float requestedMax, out float nextMin, out float nextMax)
        {
            nextMin = currentMin; nextMax = currentMax;
            bool stillOurs = active && currentMin == appliedMin && currentMax == appliedMax;
            if (!enabled)
            {
                active = false;
                if (!stillOurs) return false;
                nextMin = originalMin; nextMax = originalMax;
                return nextMin != currentMin || nextMax != currentMax;
            }
            if (!stillOurs) { originalMin = currentMin; originalMax = currentMax; }
            active = true;
            appliedMin = requestedMin; appliedMax = requestedMax;
            nextMin = requestedMin; nextMax = requestedMax;
            return nextMin != currentMin || nextMax != currentMax;
        }
    }
}
