using HoloToolkit.Sharing;
using HoloToolkit.Unity;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VR.WSA.Persistence;
using UnityEngine.VR.WSA.Sharing;
using System;
using UnityEngine.VR.WSA;
using HoloToolkit.Unity.SpatialMapping;
using System.Text;

public class ImportExportAnchorManager240 : Singleton<ImportExportAnchorManager240>
{
    /// <summary>
    /// 建立共享坐标系过程中的各种状态
    /// </summary>
    private enum ImportExportState
    {
        // 整体状态
        /// <summary>
        /// 开始
        /// </summary>
        Start,
        /// <summary>
        /// 已完成
        /// </summary>
        Ready,
        /// <summary>
        /// 失败
        /// </summary>
        Failed,
        // 本地锚点存储器状态
        /// <summary>
        /// 本地锚点存储器正在初始化
        /// </summary>
        AnchorStore_Initializing,
        /// <summary>
        /// 本地锚点存储器已初始化完成（在状态机中）
        /// </summary>
        AnchorStore_Initialized,
        /// <summary>
        /// 房间API已初始化完成（在状态机中）
        /// </summary>
        RoomApiInitialized,
        // Anchor creation values
        /// <summary>
        /// 需要初始锚点（在状态机中）
        /// </summary>
        InitialAnchorRequired,
        /// <summary>
        /// 正在创建初始锚点
        /// </summary>
        CreatingInitialAnchor,
        /// <summary>
        /// 准备导出初始锚点（在状态机中）
        /// </summary>
        ReadyToExportInitialAnchor,
        /// <summary>
        /// 正在上传初始锚点
        /// </summary>
        UploadingInitialAnchor,
        // Anchor values
        /// <summary>
        /// 已请求数据
        /// </summary>
        DataRequested,
        /// <summary>
        /// 数据已准备（在状态机中）
        /// </summary>
        DataReady,
        /// <summary>
        /// 导入中
        /// </summary>
        Importing
    }

    /// <summary>
    /// 当前状态
    /// </summary>
    private ImportExportState currentState = ImportExportState.Start;

    /// <summary>
    /// 上次状态，用来测试的，代码在Update中
    /// </summary>
    private ImportExportState lastState = ImportExportState.Start;

    /// <summary>
    /// 当前状态名
    /// </summary>
    public string StateName
    {
        get
        {
            return currentState.ToString();
        }
    }

    /// <summary>
    /// 共享坐标系是否已经建立完成
    /// </summary>
    public bool AnchorEstablished
    {
        get
        {
            return currentState == ImportExportState.Ready;
        }
    }

    /// <summary>
    /// 序列化坐标锚点并进行设备间的传输
    /// </summary>
    private WorldAnchorTransferBatch sharedAnchorInterface;

    /// <summary>
    /// 下载的原始锚点数据
    /// </summary>
    private byte[] rawAnchorData = null;

    /// <summary>
    /// 本地锚点存储器
    /// </summary>
    private WorldAnchorStore anchorStore = null;

    /// <summary>
    /// 保存我们正在导出的锚点名称
    /// </summary>
    public string ExportingAnchorName = "anchor-1234567890";

    /// <summary>
    /// 正在导出的锚点数据
    /// </summary>
    private List<byte> exportingAnchorBytes = new List<byte>();

    /// <summary>
    /// 共享服务是否已经准备好，这个是上传和下载锚点数据的前提条件
    /// </summary>
    private bool sharingServiceReady = false;

    /// <summary>
    /// 共享服务中的房间管理器
    /// </summary>
    private RoomManager roomManager;

    /// <summary>
    /// 当前房间（锚点将会保存在房间中）
    /// </summary>
    private Room currentRoom;

    /// <summary>
    /// 有时我们会发现一些很小很小的锚点数据，这些往往没法使用，所以我们设置一个最小的可信任大小值
    /// </summary>
    private const uint minTrustworthySerializedAnchorDataSize = 100000;

    /// <summary>
    /// 房间编号
    /// </summary>
    private const long roomID = 8675309;

    /// <summary>
    /// 房间管理器的各种事件监听
    /// </summary>
    private RoomManagerAdapter roomManagerCallbacks;

    /// <summary>
    /// 锚点上传完成事件
    /// </summary>
    public event Action<bool> AnchorUploaded;

    /// <summary>
    /// 锚点加载完成事件
    /// </summary>
    public event Action AnchorLoaded;

    private TextMesh lblMsg;
    private StringBuilder sb = new StringBuilder();
    private void debug(string msg)
    {
        Debug.Log(msg);
        sb.AppendLine(msg);
    }

    protected override void Awake()
    {
        base.Awake();

        lblMsg = GameObject.Find("FPSText").GetComponent<TextMesh>();

        // 开始初始化本地锚点存储器
        currentState = ImportExportState.AnchorStore_Initializing;
        WorldAnchorStore.GetAsync(AnchorStoreReady);
    }

    /// <summary>
    /// 本地锚点存储器已准备好
    /// </summary>
    /// <param name="store">本地锚点存储器</param>
    private void AnchorStoreReady(WorldAnchorStore store)
    {
        debug("本地锚点存储器（WorldAnchorStore）已准备好 - AnchorStoreReady(WorldAnchorStore store)");

        anchorStore = store;
        currentState = ImportExportState.AnchorStore_Initialized;
    }

    private void Start()
    {

        bool isObserverRunning = SpatialMappingManager.Instance.IsObserverRunning();
        debug("空间扫描状态：" + isObserverRunning);

        if (!isObserverRunning)
        {
            SpatialMappingManager.Instance.StartObserver();
        }

        // 共享管理器是否已经连接
        SharingStage.Instance.SharingManagerConnected += Instance_SharingManagerConnected;

        // 是否加入到当前会话中（此事件在共享管理器连接之后才会触发）
        SharingStage.Instance.SessionsTracker.CurrentUserJoined += SessionsTracker_CurrentUserJoined;
        SharingStage.Instance.SessionsTracker.CurrentUserLeft += SessionsTracker_CurrentUserLeft;
    }



    #region 共享管理器连接成功后的一系列处理

    // 共享管理器连接事件
    private void Instance_SharingManagerConnected(object sender, EventArgs e)
    {
        debug("共享管理器连接成功 - Instance_SharingManagerConnected(object sender, EventArgs e)");

        // 从共享管理器中获取房间管理器
        roomManager = SharingStage.Instance.Manager.GetRoomManager();

        // 房间管理器的事件监听
        roomManagerCallbacks = new RoomManagerAdapter();

        // 房间中锚点下载完成事件
        roomManagerCallbacks.AnchorsDownloadedEvent += RoomManagerCallbacks_AnchorsDownloadedEvent;
        // 房间中锚点上传完成事件
        roomManagerCallbacks.AnchorUploadedEvent += RoomManagerCallbacks_AnchorUploadedEvent;

        // 为房间管理器添加上面的事件监听
        roomManager.AddListener(roomManagerCallbacks);
    }

    // 房间中锚点上传完成事件
    private void RoomManagerCallbacks_AnchorUploadedEvent(bool successful, XString failureReason)
    {
        if (successful)
        {
            debug("房间锚点上传完成 - RoomManagerCallbacks_AnchorUploadedEvent(bool successful, XString failureReason)");

            // 房间锚点上传成功后，空间坐标共享机制建立完成
            currentState = ImportExportState.Ready;
        }
        else
        {
            debug("房间锚点上传失败 - RoomManagerCallbacks_AnchorUploadedEvent(bool successful, XString failureReason)");

            // 房间锚点上传失败
            debug("Anchor Upload Failed!" + failureReason);
            currentState = ImportExportState.Failed;
        }

        if (AnchorUploaded != null)
        {
            AnchorUploaded(successful);
        }
    }

    // 房间中锚点下载完成事件
    private void RoomManagerCallbacks_AnchorsDownloadedEvent(bool successful, AnchorDownloadRequest request, XString failureReason)
    {
        if (successful)
        {
            debug("房间锚点下载完成 - RoomManagerCallbacks_AnchorsDownloadedEvent(bool successful, AnchorDownloadRequest request, XString failureReason)");

            // 房间锚点下载完成
            // 获取锚点数据长度
            int datasize = request.GetDataSize();

            // 将下载的锚点数据缓存到数组中
            rawAnchorData = new byte[datasize];

            request.GetData(rawAnchorData, datasize);

            // 保存完锚点数据，可以开始准备传输数据
            currentState = ImportExportState.DataReady;
        }
        else
        {
            debug("锚点下载失败！" + failureReason + " - RoomManagerCallbacks_AnchorsDownloadedEvent(bool successful, AnchorDownloadRequest request, XString failureReason)");

            // 锚点下载失败，重新开始请求锚点数据
            MakeAnchorDataRequest();
        }
    }

    /// <summary>
    /// 请求锚点数据
    /// </summary>
    private void MakeAnchorDataRequest()
    {
        if (roomManager.DownloadAnchor(currentRoom, new XString(ExportingAnchorName)))
        {
            // 下载锚点完成
            currentState = ImportExportState.DataRequested;
        }
        else
        {
            currentState = ImportExportState.Failed;
        }
    }

    #endregion

    #region 成功加入当前会话后的一系列处理

    // 加入当前会话完成
    private void SessionsTracker_CurrentUserJoined(Session session)
    {
        SharingStage.Instance.SessionsTracker.CurrentUserJoined -= SessionsTracker_CurrentUserJoined;

        // 稍等一下，将共享服务状态设置为正常，即可以开始同步锚点了
        Invoke("MarkSharingServiceReady", 5);
    }

    // 退出当前会话
    private void SessionsTracker_CurrentUserLeft(Session session)
    {
        sharingServiceReady = false;
        if (anchorStore != null)
        {
            currentState = ImportExportState.AnchorStore_Initialized;
        }
        else
        {
            currentState = ImportExportState.AnchorStore_Initializing;
        }
    }

    /// <summary>
    /// 将共享服务状态设置为正常
    /// </summary>
    private void MarkSharingServiceReady()
    {
        sharingServiceReady = true;


#if UNITY_EDITOR || UNITY_STANDALONE

        InitRoomApi();

#endif

    }

    /// <summary>
    /// 初始化房间，直到加入到房间中（Update中会持续调用）
    /// </summary>
    private void InitRoomApi()
    {
        int roomCount = roomManager.GetRoomCount();

        if (roomCount == 0)
        {
            debug("未找到房间 - InitRoomApi()");

            // 如果当前会话中，没有获取到任何房间
            if (LocalUserHasLowestUserId())
            {
                // 如果当前用户编号最小，则创建房间
                currentRoom = roomManager.CreateRoom(new XString("DefaultRoom"), roomID, false);
                // 房间创建好，准备加载本地的初始锚点，供其他人共享
                currentState = ImportExportState.InitialAnchorRequired;

                debug("我是房主，创建房间完成 - InitRoomApi()");
            }
        }
        else
        {
            for (int i = 0; i < roomCount; i++)
            {
                currentRoom = roomManager.GetRoom(i);
                if (currentRoom.GetID() == roomID)
                {
                    // 加入当前房间
                    roomManager.JoinRoom(currentRoom);
                    // TODO: 加入房间，房间API初始化完成，准备同步初始锚点
                    currentState = ImportExportState.RoomApiInitialized;

                    debug("找到房间并加入！ - InitRoomApi()");

                    return;
                }
            }
        }
    }

    /// <summary>
    /// 判断当前用户编号是不是所有用户中最小的
    /// </summary>
    /// <returns></returns>
    private bool LocalUserHasLowestUserId()
    {
        if (SharingStage.Instance == null)
        {
            return false;
        }
        if (SharingStage.Instance.SessionUsersTracker != null)
        {
            List<User> currentUsers = SharingStage.Instance.SessionUsersTracker.CurrentUsers;
            for (int i = 0; i < currentUsers.Count; i++)
            {
                if (currentUsers[i].GetID() < CustomMessages240.Instance.LocalUserID)
                {
                    return false;
                }
            }
        }
        return true;
    }

    #endregion

    // Update中处理各种状态（简单状态机）
    private void Update()
    {
        if (currentState != lastState)
        {
            debug("状态变化：" + lastState.ToString() + " > " + currentState.ToString());
            lastState = currentState;
        }

        lblMsg.text = sb.ToString();

        switch (currentState)
        {
            case ImportExportState.AnchorStore_Initialized:
                // 本地锚点存储器初始化完成
                // 如果成功加入当前会话，则开始加载房间
                if (sharingServiceReady)
                {
                    InitRoomApi();
                }
                break;
            case ImportExportState.RoomApiInitialized:
                // 房间已加载完成，开始加载锚点信息
                StartAnchorProcess();
                break;
            case ImportExportState.DataReady:
                // 锚点数据下载完成后，开始导入锚点数据
                currentState = ImportExportState.Importing;
                WorldAnchorTransferBatch.ImportAsync(rawAnchorData, ImportComplete);
                break;
            case ImportExportState.InitialAnchorRequired:
                // 房主房间创建完成后，需要创建初始锚点共享给他人
                currentState = ImportExportState.CreatingInitialAnchor;
                // 创建本地锚点
                CreateAnchorLocally();
                break;
            case ImportExportState.ReadyToExportInitialAnchor:
                // 准备导出初始锚点
                currentState = ImportExportState.UploadingInitialAnchor;
                // 执行导出
                Export();
                break;
        }
    }

    /// <summary>
    /// 房主将本地锚点共享给其他人
    /// </summary>
    private void Export()
    {
        // 获取锚点，这个组件会在CreateAnchorLocally()中自动添加
        WorldAnchor anchor = GetComponent<WorldAnchor>();

        anchorStore.Clear();
        // 本地保存该锚点
        if (anchor != null && anchorStore.Save(ExportingAnchorName, anchor))
        {
            debug("保存锚点完成，准备导出！ - Export()");
            // 将锚点导出
            sharedAnchorInterface = new WorldAnchorTransferBatch();
            sharedAnchorInterface.AddWorldAnchor(ExportingAnchorName, anchor);
            WorldAnchorTransferBatch.ExportAsync(sharedAnchorInterface, WriteBuffer, ExportComplete);
        }
        else
        {
            debug("保存本地锚点失败！ - Export()");

            currentState = ImportExportState.InitialAnchorRequired;
        }
    }

    /// <summary>
    /// 房主导出锚点成功
    /// </summary>
    /// <param name="completionReason"></param>
    private void ExportComplete(SerializationCompletionReason completionReason)
    {
        if (completionReason == SerializationCompletionReason.Succeeded && exportingAnchorBytes.Count > minTrustworthySerializedAnchorDataSize)
        {
            // 将锚点数据上传至当前房间中
            roomManager.UploadAnchor(
                currentRoom,
                new XString(ExportingAnchorName),
                exportingAnchorBytes.ToArray(),
                exportingAnchorBytes.Count);
        }
        else
        {
            debug("导出锚点出错！" + completionReason.ToString());
            currentState = ImportExportState.InitialAnchorRequired;
        }
    }

    private void WriteBuffer(byte[] data)
    {
        exportingAnchorBytes.AddRange(data);
    }

    /// <summary>
    /// 房主在本地创建一个新的锚点
    /// </summary>
    private void CreateAnchorLocally()
    {
        debug("开始创建本地锚点");

        // 添加世界锚点组件
        WorldAnchor anchor = GetComponent<WorldAnchor>();
        if (anchor == null)
        {
            anchor = gameObject.AddComponent<WorldAnchor>();
        }

        if (anchor.isLocated)
        {
            // 房主自己定位好本地锚点后，准备导出给其他人
            currentState = ImportExportState.ReadyToExportInitialAnchor;
        }
        else
        {
            anchor.OnTrackingChanged += WorldAnchorForExport_OnTrackingChanged;
        }
    }

    private void WorldAnchorForExport_OnTrackingChanged(WorldAnchor self, bool located)
    {
        if (located)
        {
            // 房主自己定位好本地锚点后，准备导出给其他人
            currentState = ImportExportState.ReadyToExportInitialAnchor;
        }
        else
        {
            // 房主自己的锚点定位失败，则同步总体失败
            currentState = ImportExportState.Failed;
        }

        self.OnTrackingChanged -= WorldAnchorForExport_OnTrackingChanged;
    }

    /// <summary>
    /// 锚点数据下载完成后，开始导入锚点数据
    /// </summary>
    /// <param name="completionReason"></param>
    /// <param name="deserializedTransferBatch"></param>
    private void ImportComplete(SerializationCompletionReason completionReason, WorldAnchorTransferBatch deserializedTransferBatch)
    {
        if (completionReason == SerializationCompletionReason.Succeeded && deserializedTransferBatch.GetAllIds().Length > 0)
        {
            // 成功导入锚点
            // 获取第一个锚点名称
            bool hasAnchorName = false;
            string[] anchorNames = deserializedTransferBatch.GetAllIds();
            foreach (var an in anchorNames)
            {
                if (an == ExportingAnchorName)
                {
                    hasAnchorName = true;
                    break;
                }
            }

            if (!hasAnchorName)
            {
                currentState = ImportExportState.DataReady;
                return;
            }

            // 保存锚点到本地
            WorldAnchor anchor = deserializedTransferBatch.LockObject(ExportingAnchorName, gameObject);
            if (anchor.isLocated)
            {
                if (anchorStore.Save(ExportingAnchorName, anchor))
                {
                    currentState = ImportExportState.Ready;
                }
                else
                {
                    currentState = ImportExportState.DataReady;
                }

            }
            else
            {
                anchor.OnTrackingChanged += WorldAnchorForImport_OnTrackingChanged;
            }
        }
        else
        {
            // 未成功导入，则设置为DataReady，准备在下一帧再次导入，直到导入完成
            currentState = ImportExportState.DataReady;
        }
    }

    private void WorldAnchorForImport_OnTrackingChanged(WorldAnchor self, bool located)
    {
        if (located)
        {
            WorldAnchor anchor = GetComponent<WorldAnchor>();
            if (anchorStore.Save(ExportingAnchorName, anchor))
            {
                currentState = ImportExportState.Ready;
            }
            else
            {
                currentState = ImportExportState.DataReady;
            }
        }
        else
        {
            currentState = ImportExportState.Failed;
        }

        self.OnTrackingChanged -= WorldAnchorForImport_OnTrackingChanged;
    }

    /// <summary>
    /// 加载锚点信息
    /// </summary>
    private void StartAnchorProcess()
    {
        debug("正在获取房间锚点…… - StartAnchorProcess()");

        // 检查当前房间有无锚点
        int anchorCount = currentRoom.GetAnchorCount();

        if (anchorCount > 0)
        {
            bool isRoomAnchorExists = false;

            for (int i = 0; i < anchorCount; i++)
            {
                string roomAnchor = currentRoom.GetAnchorName(i).GetString();
                if (roomAnchor == ExportingAnchorName)
                {
                    isRoomAnchorExists = true;
                    break;
                }
            }

            if (isRoomAnchorExists)
            {
                debug("获取房间锚点成功！开始下载锚点");
                // 获取房间锚点信息成功后，开始下载锚点数据
                MakeAnchorDataRequest();
            }
        }
    }

    protected override void OnDestroy()
    {
        if (SharingStage.Instance != null)
        {
            SharingStage.Instance.SharingManagerConnected -= Instance_SharingManagerConnected;
            if (SharingStage.Instance.SessionsTracker != null)
            {
                SharingStage.Instance.SessionsTracker.CurrentUserJoined -= SessionsTracker_CurrentUserJoined;
                SharingStage.Instance.SessionsTracker.CurrentUserLeft -= SessionsTracker_CurrentUserLeft;
            }
        }

        if (roomManagerCallbacks != null)
        {
            roomManagerCallbacks.AnchorsDownloadedEvent -= RoomManagerCallbacks_AnchorsDownloadedEvent;
            roomManagerCallbacks.AnchorUploadedEvent -= RoomManagerCallbacks_AnchorUploadedEvent;

            if (roomManager != null)
            {
                roomManager.RemoveListener(roomManagerCallbacks);
            }

            roomManagerCallbacks.Dispose();
            roomManagerCallbacks = null;
        }

        if (roomManager != null)
        {
            roomManager.Dispose();
            roomManager = null;
        }

        base.OnDestroy();
    }
}