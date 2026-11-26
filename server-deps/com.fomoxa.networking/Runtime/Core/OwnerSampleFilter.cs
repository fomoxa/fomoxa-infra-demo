namespace Fomoxa.Networking
{
    public delegate bool OwnerSampleFilter(in TransformSample accepted, in TransformSample incoming, uint ticks);
}
