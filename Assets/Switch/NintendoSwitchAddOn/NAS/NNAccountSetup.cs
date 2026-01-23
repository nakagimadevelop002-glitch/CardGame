/*--------------------------------------------------------------------------------*
  Copyright (C)Nintendo All rights reserved.

  These coded instructions, statements, and computer programs contain proprietary
  information of Nintendo and/or its licensed developers and are protected by
  national and international copyright laws. They may not be disclosed to third
  parties or copied or duplicated in any form, in whole or in part, without the
  prior written consent of Nintendo.

  The content herein is highly confidential and should be handled accordingly.
 *--------------------------------------------------------------------------------*/

// -----------------------------------------------------------------------
// プラットフォーム依存コード用のマクロ
// 該当プラットフォームの実行時またはビルド時のみ有効にするためのマクロです。
// -----------------------------------------------------------------------
#if (UNITY_EDITOR || UNITY_STANDALONE)
// エディタ実行またはスタンドアローンビルド時のみ
// エディタ実行を行う場合は Switch Platform の設定に関係なく下記のマクロが有効になります。
#define UNITY_EDITOR_OR_STANDALONE
#endif

#if ((UNITY_SWITCH || UNITY_SWITCH2) && !UNITY_EDITOR_OR_STANDALONE)
// SWITCH ビルド時のみ
#define UNITY_ONLY_SWITCH
#endif

//using nn.pia;

public static class NNAccountSetup
{
#if UNITY_SWITCH || UNITY_SWITCH2
    private static nn.account.Uid s_Uid;
    public static nn.account.Uid Uid
    {
        get
        {
            return s_Uid;
        }
    }

    private static nn.account.UserHandle s_UserHandle;
    public static nn.account.UserHandle UserHandle
    {
        get
        {
            return s_UserHandle;
        }
    }

    private static nn.account.NetworkServiceAccountId s_NsaId;
    public static nn.account.NetworkServiceAccountId NsaId
    {
        get
        {
            return s_NsaId;
        }
    }

    private static nn.Result result;
    public static bool IsSuccess
    {
        get
        {
            return result.IsSuccess();
        }
    }

    public static bool GetNsaId()
    {
        nn.Result result = new nn.Result();
        nn.account.Account.Initialize();

        SwitchOneTwo.Extentions.nn_AppletWrapper.LeaveNetworkConnecting();
        SwitchOneTwo.Extentions.nn_AppletWrapper.EnterNetworkConnecting(false ,true);
        SwitchOneTwo.Extentions.nn_AppletWrapper.WaitForNetworkConnecting();

        while (true)
        {
            result = nn.account.NetworkServiceAccount.EnsureAvailable(s_UserHandle);
            if(!result.IsSuccess())
            {
                //nn.err.Error.Show(result);
                return false;
            }

            result = nn.account.NetworkServiceAccount.GetId(ref s_NsaId, s_UserHandle);
            if(!result.IsSuccess())
            {
                if(nn.account.NetworkServiceAccount.ResultNetworkServiceAccountUnavailable.Includes(result))
                {
                    continue;
                }
                nn.err.Error.Show(result);
                return false;
            }

            if(!SwitchOneTwo.Extentions.nn_AppletWrapper.IsNetworkAccepted() && 
               !SwitchOneTwo.Extentions.nn_AppletWrapper.IsNetworkAvailable())
            {
                return false;
            }

            nn.account.AsyncContext pOutContext = new nn.account.AsyncContext();
            result = nn.account.NetworkServiceAccount.EnsureIdTokenCacheAsync(pOutContext, s_UserHandle);
            if(!result.IsSuccess())
            {
                if (nn.account.NetworkServiceAccount.ResultNetworkServiceAccountUnavailable.Includes(result))
                {
                    continue;
                }
                nn.err.Error.Show(result);
                return false;
            }

            bool done = false;
            while(!done)
            {
                System.Threading.Thread.Sleep(100);
                pOutContext.HasDone(ref done);
            }
            result = pOutContext.GetResult();
            if (!result.IsSuccess())
            {
                if (nn.account.NetworkServiceAccount.ResultNetworkServiceAccountUnavailable.Includes(result))
                {
                    continue;
                }
                nn.err.Error.Show(result);
                return false;
            }
            long size = 0;
            byte[] token = new byte[nn.account.NetworkServiceAccount.IdTokenLengthMax];
            result = nn.account.NetworkServiceAccount.LoadIdTokenCache(ref size, token, s_UserHandle);
            UnityEngine.Debug.LogFormat("LoadIdTokenCache {0}", result);
            if(!result.IsSuccess())
            {
                if(nn.account.NetworkServiceAccount.ResultNetworkServiceAccountUnavailable.Includes(result))
                {
                    continue;
                }
                if(nn.account.NetworkServiceAccount.ResultTokenCacheUnavailable.Includes(result))
                {
                    continue;
                }
                nn.err.Error.Show(result);
            }

            return true;
        }
    }

    public static nn.Result OpenUserAuto()
    {
        
        nn.account.Account.Initialize();

        // Uid の一覧を取得します
        nn.account.Uid[] uidList = new nn.account.Uid[nn.account.Account.UserCountMax];
        int userAccountNum = 0;
        result = nn.account.Account.ListQualifiedUsers(ref userAccountNum, uidList, nn.account.Account.UserCountMax);
        if(!result.IsSuccess())
        {
            //nn.pia.Log.TracePiaUnity("ListQualifiedUsers is failed. ErrorCode : " + result.innerValue);
            //Assertion.Assert(false);
            return result;
        }

        // アカウントをオープン状態に変更します
        for (int i = 0; i < userAccountNum; ++i)
        {
            result = nn.account.Account.OpenUser(ref s_UserHandle, uidList[i]);
            if (!result.IsSuccess())
            {
                //nn.pia.Log.TracePiaUnity("OpenUser is failed. ErrorCode : " + result.innerValue);
                continue;
            }

            // 最初に見つかった有効な Uid を採用
            s_Uid = uidList[i];
            break;
        }

        // NetworkServiceAccountId を取得します
        result = nn.account.NetworkServiceAccount.GetId(ref s_NsaId, s_UserHandle);
        if (!result.IsSuccess())
        {
            //nn.pia.Log.TracePiaUnity("GetId is failed. ErrorCode : " + result.innerValue);
            //Assertion.Assert(false);
            return result;
        }

        return result;
    }

    public static nn.Result OpenUserWithShowUserSelector()
    {
        nn.Result result;
        nn.account.Account.Initialize();

        // ユーザー選択画面を開きます
        result = nn.account.Account.ShowUserSelector(ref s_Uid);
        if (!result.IsSuccess())
        {
            //nn.pia.Log.TracePiaUnity("ShowUserSelector is failed. ErrorCode : " + result.innerValue);
            // このサンプルではどのエラー（Bでユーザを選ばなかった時）でもエラーコード画面を表示しています
            // 製品ではエラーによって適切なエラー処理が異なることに注意してください
            nn.err.Error.Show(result);
            return result;
        }

        // アカウントをオープン状態に変更します
        result = nn.account.Account.OpenUser(ref s_UserHandle, s_Uid);
        if (!result.IsSuccess())
        {
            //nn.pia.Log.TracePiaUnity($"OpenUser is failed. Result:{result.ToString()}");
            //Assertion.Assert(false);
            return result;
        }

        // NetworkServiceAccountId を取得します
        nn.account.NetworkServiceAccount.GetId(ref s_NsaId, s_UserHandle);
        if (!result.IsSuccess())
        {
            //nn.pia.Log.TracePiaUnity("GetId is failed. ErrorCode : " + result.innerValue);
            //Assertion.Assert(false);
            return result;
        }

        return result;
    }

    public static nn.Result OpenPreselectedUser()
    {
        nn.account.Account.Initialize();

        var openResult = nn.account.Account.TryOpenPreselectedUser(ref s_UserHandle);

        // NetworkServiceAccountId を取得します
/*        result = nn.account.NetworkServiceAccount.GetId(ref s_NsaId, s_UserHandle);
        if (!result.IsSuccess())
        {
            return result;
        }*/

        return result;
    }


    public static void CloseUser()
    {
        nn.account.Account.CloseUser(s_UserHandle);
    }
#endif
}
