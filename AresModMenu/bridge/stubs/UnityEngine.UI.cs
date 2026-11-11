namespace UnityEngine.EventSystems
{
    public interface IEventSystemHandler {}
    public interface IPointerDownHandler : IEventSystemHandler {}
    public interface IPointerUpHandler : IEventSystemHandler {}
    public interface IDragHandler : IEventSystemHandler {}
}
