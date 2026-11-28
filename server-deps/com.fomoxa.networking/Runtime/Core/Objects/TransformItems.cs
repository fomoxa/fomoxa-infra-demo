using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Fomoxa.Networking.Objects
{
    internal readonly struct TransformItem
    {
        public TransformItem(uint objectId, byte behaviourIndex, byte generation, byte mask, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            ObjectId = objectId;
            BehaviourIndex = behaviourIndex;
            Generation = generation;
            Mask = mask;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public uint ObjectId { get; }

        public byte BehaviourIndex { get; }

        public byte Generation { get; }

        public byte Mask { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }
    }

    internal static class TransformItems
    {
        public const int MaxLength = HeaderLength + 1 + (3 * sizeof(float)) + sizeof(uint) + (3 * sizeof(float));

        private const int HeaderLength = 7;
        private const int PositionFloatShift = 3;
        private const byte ReservedFlags = 0xC0;
        private const byte ReservedScaleFlags = 0xF8;
        private const byte AxisBits = 0x07;
        private const double AxisScale = 100.0;
        private const double ComponentScale = 511.0 * 1.4142135623730951;
        private const int ComponentOffset = 511;
        private const int ComponentMax = 1022;
        private const int ComponentBits = 10;
        private const uint ComponentMask = (1u << ComponentBits) - 1;
        private const int LargestShift = 30;

        public static int Write(Span<byte> destination, uint objectId, byte behaviourIndex, byte generation, byte mask, in Vector3 position, in Quaternion rotation, in Vector3 scale)
        {
            if ((mask & ~SpawnTransform.AllBits) != 0)
            {
                throw new ArgumentException($"mask 0x{mask:X2} has bits outside position, rotation and scale", nameof(mask));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination, objectId);
            destination[4] = behaviourIndex;
            destination[5] = generation;
            int length = HeaderLength;
            int scaleFlagsAt = 0;
            if ((mask & SpawnTransform.ScaleBit) != 0)
            {
                scaleFlagsAt = length++;
            }

            byte flags = mask;
            if ((mask & SpawnTransform.PositionBit) != 0)
            {
                flags |= (byte)(WriteVector(destination, ref length, position) << PositionFloatShift);
            }

            if ((mask & SpawnTransform.RotationBit) != 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(length), PackRotation(rotation));
                length += sizeof(uint);
            }

            if ((mask & SpawnTransform.ScaleBit) != 0)
            {
                destination[scaleFlagsAt] = WriteVector(destination, ref length, scale);
            }

            destination[6] = flags;
            return length;
        }

        public static bool TryRead(ReadOnlySpan<byte> data, ref int offset, out TransformItem item)
        {
            item = default;
            if (data.Length - offset < HeaderLength)
            {
                return false;
            }

            ReadOnlySpan<byte> header = data.Slice(offset);
            byte flags = header[6];
            if ((flags & ReservedFlags) != 0)
            {
                return false;
            }

            byte mask = (byte)(flags & SpawnTransform.AllBits);
            int position = offset + HeaderLength;
            byte scaleFlags = 0;
            if ((mask & SpawnTransform.ScaleBit) != 0)
            {
                if (position >= data.Length)
                {
                    return false;
                }

                scaleFlags = data[position++];
                if ((scaleFlags & ReservedScaleFlags) != 0)
                {
                    return false;
                }
            }

            Vector3 localPosition = Vector3.Zero;
            Quaternion localRotation = Quaternion.Identity;
            Vector3 localScale = Vector3.One;
            if ((mask & SpawnTransform.PositionBit) != 0 && !TryReadVector(data, ref position, (flags >> PositionFloatShift) & AxisBits, out localPosition))
            {
                return false;
            }

            if ((mask & SpawnTransform.RotationBit) != 0)
            {
                if (data.Length - position < sizeof(uint))
                {
                    return false;
                }

                localRotation = UnpackRotation(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position)));
                position += sizeof(uint);
            }

            if ((mask & SpawnTransform.ScaleBit) != 0 && !TryReadVector(data, ref position, scaleFlags & AxisBits, out localScale))
            {
                return false;
            }

            item = new TransformItem(BinaryPrimitives.ReadUInt32LittleEndian(header), header[4], header[5], mask, localPosition, localRotation, localScale);
            offset = position;
            return true;
        }

        public static bool IsWellFormed(ReadOnlySpan<byte> data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                if (!TryRead(data, ref offset, out _))
                {
                    return false;
                }
            }

            return true;
        }

        public static uint PackRotation(in Quaternion rotation)
        {
            Quaternion unit = Normalized(rotation);
            Span<float> components = stackalloc float[4] { unit.X, unit.Y, unit.Z, unit.W };
            int largest = 0;
            for (int index = 1; index < 4; index++)
            {
                if (Math.Abs(components[index]) > Math.Abs(components[largest]))
                {
                    largest = index;
                }
            }

            double sign = components[largest] < 0 ? -1.0 : 1.0;
            uint packed = (uint)largest << LargestShift;
            int shift = 2 * ComponentBits;
            for (int index = 0; index < 4; index++)
            {
                if (index == largest)
                {
                    continue;
                }

                int value = (int)Math.Round(components[index] * sign * ComponentScale) + ComponentOffset;
                packed |= (uint)Math.Min(Math.Max(value, 0), ComponentMax) << shift;
                shift -= ComponentBits;
            }

            return packed;
        }

        public static Quaternion UnpackRotation(uint packed)
        {
            int largest = (int)(packed >> LargestShift);
            Span<float> components = stackalloc float[4];
            int shift = 2 * ComponentBits;
            double sum = 0;
            for (int index = 0; index < 4; index++)
            {
                if (index == largest)
                {
                    continue;
                }

                double value = ((int)((packed >> shift) & ComponentMask) - ComponentOffset) / ComponentScale;
                components[index] = (float)value;
                sum += value * value;
                shift -= ComponentBits;
            }

            components[largest] = (float)Math.Sqrt(Math.Max(0.0, 1.0 - sum));
            return Normalized(new Quaternion(components[0], components[1], components[2], components[3]));
        }

        public static bool TryQuantize(float value, out short quantized)
        {
            quantized = 0;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }

            double rounded = Math.Round(value * AxisScale);
            if (Math.Abs(rounded) > short.MaxValue)
            {
                return false;
            }

            quantized = (short)rounded;
            return true;
        }

        private static Quaternion Normalized(in Quaternion rotation)
        {
            float length = rotation.Length();
            return length > 0f && !float.IsInfinity(length) && !float.IsNaN(length) ? Quaternion.Multiply(rotation, 1f / length) : Quaternion.Identity;
        }

        private static byte WriteVector(Span<byte> destination, ref int length, in Vector3 vector)
        {
            byte floats = 0;
            if (WriteAxis(destination, ref length, vector.X))
            {
                floats |= 1;
            }

            if (WriteAxis(destination, ref length, vector.Y))
            {
                floats |= 2;
            }

            if (WriteAxis(destination, ref length, vector.Z))
            {
                floats |= 4;
            }

            return floats;
        }

        private static bool WriteAxis(Span<byte> destination, ref int length, float value)
        {
            if (TryQuantize(value, out short quantized))
            {
                BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(length), quantized);
                length += sizeof(short);
                return false;
            }

            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(length), BitConverter.SingleToInt32Bits(value));
            length += sizeof(float);
            return true;
        }

        private static bool TryReadVector(ReadOnlySpan<byte> data, ref int position, int floats, out Vector3 vector)
        {
            vector = default;
            if (!TryReadAxis(data, ref position, (floats & 1) != 0, out float x)
                || !TryReadAxis(data, ref position, (floats & 2) != 0, out float y)
                || !TryReadAxis(data, ref position, (floats & 4) != 0, out float z))
            {
                return false;
            }

            vector = new Vector3(x, y, z);
            return true;
        }

        private static bool TryReadAxis(ReadOnlySpan<byte> data, ref int position, bool isFloat, out float value)
        {
            value = 0f;
            int size = isFloat ? sizeof(float) : sizeof(short);
            if (data.Length - position < size)
            {
                return false;
            }

            value = isFloat
                ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(position)))
                : (float)(BinaryPrimitives.ReadInt16LittleEndian(data.Slice(position)) / AxisScale);
            position += size;
            return true;
        }
    }
}
