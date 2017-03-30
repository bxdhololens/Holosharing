using HoloToolkit.Sharing;
using HoloToolkit.Unity.InputModule;
using UnityEngine;

public class Cube240 : MonoBehaviour, IInputClickHandler
{

    // 是否正在移动
    bool isMoving = false;

    // 消息传递类
    CustomMessages240 customMessage;

    private void Start()
    {
        customMessage = CustomMessages240.Instance;

        // 指定收到Cube位置消息后的处理方法
        customMessage.MessageHandlers[CustomMessages240.CustomMessageID.CubePosition] = OnCubePositionReceived;
    }

    private void OnCubePositionReceived(NetworkInMessage msg)
    {
        // 同步Cube位置
        if (!isMoving)
        {
            transform.position = CustomMessages240.ReadCubePosition(msg);
        }
    }

    // 单击Cube，切换是否移动
    public void OnInputClicked(InputClickedEventData eventData)
    {
        isMoving = !isMoving;
        // 放置Cube后，发送Cube的位置消息给其他人
        if (!isMoving)
        {
            customMessage.SendCubePosition(transform.position);
        }
    }

    // 如果Cube为移动状态，让其放置在镜头前2米位置
    void Update()
    {
        if (isMoving)
        {
            transform.position = Camera.main.transform.position + Camera.main.transform.forward * 2f;
            // 实时传递Cube位置
            customMessage.SendCubePosition(transform.position, MessageReliability.UnreliableSequenced);
        }
    }
}