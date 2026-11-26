using System.Numerics;

namespace Fomoxa.Networking
{
    public interface ITransformWriter
    {
        void WriteLocal(in Vector3 localPosition, in Quaternion localRotation, in Vector3 localScale);
    }
}
