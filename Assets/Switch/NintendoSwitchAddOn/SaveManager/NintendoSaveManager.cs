#if UNITY_SWITCH || UNITY_SWITCH2
// SWITCH ビルド時のみ
#define UNITY_SWITCH1_2
#endif

using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SwitchOneTwo.Extentions;


/// <summary>
/// NintendoSwitchでのセーブロードを管理するクラス
/// </summary>
public static class NintendoSaveManager
{
#if UNITY_SWITCH1_2
    private readonly static string MOUNT_NAME = "Winter";
    private readonly static int SLOT_DATA_SIZE = 240 * 1024; // 240KiB
    private readonly static int SETTING_DATA_SIZE = 9216 * 1024; // 9MgB
    private readonly static int SAVE_VERSION = 0;
    private static nn.account.Uid userId;
    private static nn.fs.FileHandle fileHandle = new nn.fs.FileHandle();
    private static bool isInitialized = false;
    private static NintendoNotificationWrapper nintendoNotificationWrapper = new NintendoNotificationWrapper();
#endif

    public static void Init()
    {
        Debug.Log("NintendoSaveManager::Init");
#if UNITY_SWITCH1_2
        Debug.Log("init start");
        if (isInitialized)
        {
            return;
        }
        Debug.Log("isInitialized after");

        nn.account.Account.Initialize();
        Debug.Log("nn.account.Account.Initialize()");
        nn.account.UserHandle userHandle = new nn.account.UserHandle();
        Debug.Log("nn.account.UserHandle()");

        var openUserResult = NNAccountSetup.OpenPreselectedUser();
        //var openUserResult = NNAccountSetup.OpenUserAuto();

        if (!openUserResult.IsSuccess())
        {
            nn.Nn.Abort("Failed to open preselected user.");
        }
        Debug.Log("nn.account.Account.TryOpenPreselectedUser");
        nn.Result result = nn.account.Account.GetUserId(ref userId, NNAccountSetup.UserHandle);
        Debug.Log("nn.account.Account.GetUserId after");
        result.abortUnlessSuccess();
        Debug.Log("abortUnlessSuccess after 1:"+ userId.ToString());

        result = nn.fs.SaveData.Ensure(userId);
        result = nn.fs.SaveData.Mount(MOUNT_NAME, userId);
        Debug.Log("nn.fs.SaveData.Mount");
        result.abortUnlessSuccess();
        Debug.Log("abortUnlessSuccess after 2");
        Debug.Log($"mountName={MOUNT_NAME}, _userId={userId}");
        isInitialized = true;
#endif
    }

    /// <summary>
    /// Switch本体のセーブパスを返す
    /// </summary>
    /// <param name="fileName">拡張子込みのファイル名</param>
    /// <returns></returns>
    public static string GetSavePath(string fileName)
    {
#if UNITY_SWITCH1_2
        return $"{MOUNT_NAME}:/{fileName}";
#endif
        return "";
    }

    /// <summary>
    /// スロットデータが存在するか
    /// </summary>
    /// <param name="slot"></param>
    /// <returns></returns>
    public static bool IsExistSlotData(string fileName)
    {
#if UNITY_SWITCH1_2
        nn.fs.EntryType entryType = 0;
        nn.Result result = nn.fs.FileSystem.GetEntryType(ref entryType, GetSavePath(fileName));
        return !(nn.fs.FileSystem.ResultPathNotFound.Includes(result));
#endif
        return false;
    }

    public static void SaveSlot(string fileName, string slotJson)
    {
#if UNITY_SWITCH1_2
        byte[] data;
        using (MemoryStream stream = new MemoryStream(SLOT_DATA_SIZE))
        {
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(SAVE_VERSION);
            writer.Write(slotJson);
            stream.Close();
            data = stream.GetBuffer();
            Debug.Assert(data.Length == SLOT_DATA_SIZE);
        }

        // Nintendo Switch Guideline 0080
        nintendoNotificationWrapper.EnterExitRequestHandlingSection();

        var filePath = GetSavePath(fileName);
        Debug.Log($"FilePath={filePath}");
        nn.Result result = nn.fs.File.Delete(filePath);
        if (!nn.fs.FileSystem.ResultPathNotFound.Includes(result))
        {
            result.abortUnlessSuccess();
        }

        result = nn.fs.File.Create(filePath, SLOT_DATA_SIZE);
        result.abortUnlessSuccess();

        result = nn.fs.File.Open(ref fileHandle, filePath, nn.fs.OpenFileMode.Write);
        result.abortUnlessSuccess();

        result = nn.fs.File.Write(fileHandle, 0, data, data.LongLength, nn.fs.WriteOption.Flush);
        result.abortUnlessSuccess();

        nn.fs.File.Close(fileHandle);
        result = nn.fs.FileSystem.Commit(MOUNT_NAME);
        result.abortUnlessSuccess();

        // Nintendo Switch Guideline 0080
        nintendoNotificationWrapper.LeaveExitRequestHandlingSection();
#endif
    }

    public static string LoadSlot(string fileName)
    {
#if UNITY_SWITCH1_2
        nn.fs.EntryType entryType = 0;
        var filePath = GetSavePath(fileName);
        nn.Result result = nn.fs.FileSystem.GetEntryType(ref entryType, filePath);
        if (nn.fs.FileSystem.ResultPathNotFound.Includes(result)) { return ""; }
        result.abortUnlessSuccess();

        result = nn.fs.File.Open(ref fileHandle, filePath, nn.fs.OpenFileMode.Read);
        result.abortUnlessSuccess();

        long fileSize = 0;
        result = nn.fs.File.GetSize(ref fileSize, fileHandle);
        result.abortUnlessSuccess();

        byte[] data = new byte[fileSize];
        result = nn.fs.File.Read(fileHandle, 0, data, fileSize);
        result.abortUnlessSuccess();

        nn.fs.File.Close(fileHandle);

        using (MemoryStream stream = new MemoryStream(data))
        {
            BinaryReader reader = new BinaryReader(stream);
            int version = reader.ReadInt32();
            Debug.Assert(version == SAVE_VERSION); // Save data version up
            return reader.ReadString();
        }
#endif
        return "";
    }

    /// <summary>
    /// スロットデータを削除
    /// </summary>
    /// <param name="slot"></param>
    public static void DeleteSaveData(string fileName)
    {
#if UNITY_SWITCH1_2
        // Nintendo Switch Guideline 0080
        nintendoNotificationWrapper.EnterExitRequestHandlingSection();

        var filePath = GetSavePath(fileName);
        nn.Result result = nn.fs.File.Delete(filePath);
        if (!nn.fs.FileSystem.ResultPathNotFound.Includes(result))
        {
            result.abortUnlessSuccess();
        }

        // Nintendo Switch Guideline 0080
        nintendoNotificationWrapper.LeaveExitRequestHandlingSection();
#endif
    }

    /// <summary>
    /// 複数のスロットデータをまとめて削除する
    /// </summary>
    /// <param name="fileNamesWithoutExtention"></param>
    // 終了要求ハンドリングを何度も取得するのを回避するため複数ファイル削除用のメソッドを作成しています）
    public static void DeleteSaveDatas(List<string> fileNames)
    {
#if UNITY_SWITCH1_2
        // Nintendo Switch Guideline 0080
        nintendoNotificationWrapper.EnterExitRequestHandlingSection();

        foreach(string name in fileNames)
        {
            var filePath = GetSavePath(name);
            nn.Result result = nn.fs.File.Delete(filePath);
            if (!nn.fs.FileSystem.ResultPathNotFound.Includes(result))
            {
                result.abortUnlessSuccess();
            }
        }

        // Nintendo Switch Guideline 0080
        nintendoNotificationWrapper.LeaveExitRequestHandlingSection();
#endif
    }

    /// <summary>
    /// Switch本体のシステムデータのパスを返す
    /// </summary>
    /// <param name="slot"></param>
    /// <returns></returns>
    public static string GetSettingPath()
    {
#if UNITY_SWITCH1_2
        return $"{MOUNT_NAME}:/Settings";
#endif
        return "";
    }

    /// <summary>
    /// システムデータが存在するか
    /// </summary>
    /// <returns></returns>
    public static bool IsExistSettingData()
    {
#if UNITY_SWITCH1_2
        nn.fs.EntryType entryType = 0;
        nn.Result result = nn.fs.FileSystem.GetEntryType(ref entryType, GetSettingPath());
        return !(nn.fs.FileSystem.ResultPathNotFound.Includes(result));
#endif
        return false;
    }

    public static void SaveSettingData(string systemJson)
    {
#if UNITY_SWITCH1_2
        byte[] data;
        using (MemoryStream stream = new MemoryStream(SETTING_DATA_SIZE))
        {
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(SAVE_VERSION);
            writer.Write(systemJson);
            stream.Close();
            data = stream.GetBuffer();
            Debug.Assert(data.Length == SETTING_DATA_SIZE);
        }

        // Nintendo Switch Guideline 0080
        nintendoNotificationWrapper.EnterExitRequestHandlingSection();

        var filePath = GetSettingPath();
        Debug.Log($"FilePath={filePath}");
        nn.Result result = nn.fs.File.Delete(filePath);
        if (!nn.fs.FileSystem.ResultPathNotFound.Includes(result))
        {
            result.abortUnlessSuccess();
        }

        result = nn.fs.File.Create(filePath, SETTING_DATA_SIZE);
        result.abortUnlessSuccess();

        result = nn.fs.File.Open(ref fileHandle, filePath, nn.fs.OpenFileMode.Write);
        result.abortUnlessSuccess();

        result = nn.fs.File.Write(fileHandle, 0, data, data.LongLength, nn.fs.WriteOption.Flush);
        result.abortUnlessSuccess();

        nn.fs.File.Close(fileHandle);
        result = nn.fs.FileSystem.Commit(MOUNT_NAME);
        result.abortUnlessSuccess();

        // Nintendo Switch Guideline 0080
        nintendoNotificationWrapper.LeaveExitRequestHandlingSection();
#endif
    }

    /// <summary>
    /// 設定データをJSON形式でロードする
    /// </summary>
    /// <returns>設定データのJSON</returns>
    public static string LoadSettingData()
    {
#if UNITY_SWITCH1_2
        nn.fs.EntryType entryType = 0;
        var filePath = GetSettingPath();
        nn.Result result = nn.fs.FileSystem.GetEntryType(ref entryType, filePath);
        if (nn.fs.FileSystem.ResultPathNotFound.Includes(result)) { return ""; }
        result.abortUnlessSuccess();

        result = nn.fs.File.Open(ref fileHandle, filePath, nn.fs.OpenFileMode.Read);
        result.abortUnlessSuccess();

        long fileSize = 0;
        result = nn.fs.File.GetSize(ref fileSize, fileHandle);
        result.abortUnlessSuccess();

        byte[] data = new byte[fileSize];
        result = nn.fs.File.Read(fileHandle, 0, data, fileSize);
        result.abortUnlessSuccess();

        nn.fs.File.Close(fileHandle);

        using (MemoryStream stream = new MemoryStream(data))
        {
            BinaryReader reader = new BinaryReader(stream);
            int version = reader.ReadInt32();
            Debug.Assert(version == SAVE_VERSION); // Save data version up
            return reader.ReadString();
        }
#endif
        return "";
    }
}

