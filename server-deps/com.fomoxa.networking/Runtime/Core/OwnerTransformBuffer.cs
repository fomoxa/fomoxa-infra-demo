using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking
{
    internal sealed class OwnerTransformBuffer
    {
        private readonly List<TransformSample> snapshots = new List<TransformSample>();
        private bool received;
        private uint lastTick;
        private double renderTick;

        public bool IsEmpty => snapshots.Count == 0;

        public void Reset()
        {
            snapshots.Clear();
            received = false;
            lastTick = 0;
            renderTick = 0;
        }

        public void Receive(in TransformSample sample, bool discontinuity)
        {
            if (received && !discontinuity && sample.Tick <= lastTick)
            {
                if (sample.Settle)
                {
                    InsertBarrier(sample);
                }

                return;
            }

            lastTick = sample.Tick;
            if (sample.Settle || discontinuity || !received)
            {
                received = true;
                snapshots.Clear();
                snapshots.Add(sample);
                renderTick = sample.Tick;
                return;
            }

            snapshots.Add(sample);
        }

        public TransformSample Advance(int delayTicks)
        {
            double newest = snapshots[snapshots.Count - 1].Tick;
            renderTick = Math.Max(renderTick, newest - delayTicks);
            while (snapshots.Count > 1 && snapshots[1].Tick <= renderTick)
            {
                snapshots.RemoveAt(0);
            }

            TransformSample from = snapshots[0];
            if (snapshots.Count == 1 || renderTick <= from.Tick)
            {
                return from;
            }

            TransformSample to = snapshots[1];
            float fraction = (float)((renderTick - from.Tick) / (to.Tick - from.Tick));
            return new TransformSample(
                from.Tick,
                from.Mask,
                Vector3.Lerp(from.LocalPosition, to.LocalPosition, fraction),
                Quaternion.Slerp(from.LocalRotation, to.LocalRotation, fraction),
                Vector3.Lerp(from.LocalScale, to.LocalScale, fraction),
                false,
                from.Generation);
        }

        private void InsertBarrier(in TransformSample sample)
        {
            int newer = 0;
            while (newer < snapshots.Count && snapshots[newer].Tick <= sample.Tick)
            {
                newer++;
            }

            snapshots.RemoveRange(0, newer);
            snapshots.Insert(0, sample);
        }
    }
}
