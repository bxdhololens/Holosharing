using HoloToolkit.Sharing;
using HoloToolkit.Unity;
using System.Collections.Generic;
using UnityEngine;

public class CustomMessages240 : Singleton<CustomMessages240>
{
    // 代表当前的Socket连接
    NetworkConnection serverConnection;

    // 当前连接的事件监听器，这是一个典型的适配器模式，继承自NetworkConnectionListener
    NetworkConnectionAdapter connectionAdapter;

    // 自定义消息类型
    public enum CustomMessageID : byte
    {
        // 自己的消息从MessageID.UserMessageIDStart开始编号，避免与MessageID内置消息编号冲突
        // Cube位置消息
        CubePosition = MessageID.UserMessageIDStart,
        Max
    }

    // 消息处理代理
    public delegate void MessageCallback(NetworkInMessage msg);

    // 消息处理字典
    public Dictionary<CustomMessageID, MessageCallback> MessageHandlers { get; private set; }

    // 当前用户在Sorket服务器中的唯一编号（自动生成）
    public long LocalUserID { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        // 初始化消息处理字典
        MessageHandlers = new Dictionary<CustomMessageID, MessageCallback>();
        for (byte index = (byte)MessageID.UserMessageIDStart; index < (byte)CustomMessageID.Max; index++)
        {
            if (!MessageHandlers.ContainsKey((CustomMessageID)index))
            {
                MessageHandlers.Add((CustomMessageID)index, null);
            }
        }
    }

    void Start()
    {
        // SharingStage是Sharing组件对应的脚本，内部是对经典的Socket客户端的封装。
        SharingStage.Instance.SharingManagerConnected += Instance_SharingManagerConnected;
    }

    private void Instance_SharingManagerConnected(object sender, System.EventArgs e)
    {
        // 初始化消息处理器
        InitializeMessageHandlers();
    }

    // 初始化消息处理器
    private void InitializeMessageHandlers()
    {
        SharingStage sharingStage = SharingStage.Instance;

        if (sharingStage == null)
        {
            return;
        }

        // 获取当前Socket连接
        serverConnection = sharingStage.Manager.GetServerConnection();
        if (serverConnection == null)
        {
            return;
        }

        // 初始化消息监听
        connectionAdapter = new NetworkConnectionAdapter();
        connectionAdapter.MessageReceivedCallback += ConnectionAdapter_MessageReceivedCallback;

        // 获取当前用户在Socket服务器中生成的唯一编号
        LocalUserID = sharingStage.Manager.GetLocalUser().GetID();

        // 根据每个自定义消息，添加监听器
        for (byte index = (byte)MessageID.UserMessageIDStart; index < (byte)CustomMessageID.Max; index++)
        {
            serverConnection.AddListener(index, connectionAdapter);
        }
    }

    // 接收到服务器端消息的回调处理
    private void ConnectionAdapter_MessageReceivedCallback(NetworkConnection connection, NetworkInMessage msg)
    {
        byte messageType = msg.ReadByte();
        MessageCallback messageHandler = MessageHandlers[(CustomMessageID)messageType];
        if (messageHandler != null)
        {
            messageHandler(msg);
        }
    }

    protected override void OnDestroy()
    {
        if (serverConnection != null)
        {
            for (byte index = (byte)MessageID.UserMessageIDStart; index < (byte)CustomMessageID.Max; index++)
            {
                serverConnection.RemoveListener(index, connectionAdapter);
            }
            connectionAdapter.MessageReceivedCallback -= ConnectionAdapter_MessageReceivedCallback;
        }
        base.OnDestroy();
    }

    // 创建一个Out消息（客户端传递给服务端）
    // 消息格式第一个必须为消息类型，其后再添加自己的数据
    // 我们在所有的消息一开始添加消息发送的用户编号
    private NetworkOutMessage CreateMessage(byte messageType)
    {
        NetworkOutMessage msg = serverConnection.CreateMessage(messageType);
        msg.Write(messageType);
        msg.Write(LocalUserID);
        return msg;
    }

    // 将Cube位置广播给其他用户
    public void SendCubePosition(Vector3 position, MessageReliability? reliability = MessageReliability.ReliableOrdered)
    {
        if (serverConnection != null && serverConnection.IsConnected())
        {
            // 将Cube的位置写入消息
            NetworkOutMessage msg = CreateMessage((byte)CustomMessageID.CubePosition);

            msg.Write(position.x);
            msg.Write(position.y);
            msg.Write(position.z);

            // 将消息广播给其他人
            serverConnection.Broadcast(msg,
                MessagePriority.Immediate, //立即发送
                reliability.Value, //可靠排序数据包
                MessageChannel.Default); // 默认频道
        }
    }

    // 读取Cube的位置
    public static Vector3 ReadCubePosition(NetworkInMessage msg)
    {
        // 读取用户编号，但不使用
        msg.ReadInt64();

        // 依次读取XYZ，这个和发送Cube时，写入参数顺序是一致的
        return new Vector3(msg.ReadFloat(), msg.ReadFloat(), msg.ReadFloat());
    }
}