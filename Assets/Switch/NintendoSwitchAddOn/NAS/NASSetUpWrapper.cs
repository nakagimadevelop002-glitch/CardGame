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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Switch.SwitchOneTwo
{
    public class NASSetUpWrapper
    {
#if UNITY_SWITCH || UNITY_SWITCH2

        public nn.account.Uid Uid
        {
            get
            {
#if UNITY_ONLY_SWITCH
            return NNAccountSetup.Uid;
#endif
                return default(nn.account.Uid);
            }
        }
        public nn.account.UserHandle UserHandle
        {
            get
            {
#if UNITY_ONLY_SWITCH
            return NNAccountSetup.UserHandle;
#endif
                return default(nn.account.UserHandle);
            }
        }

        public nn.account.NetworkServiceAccountId NsaId
        {
            get
            {
#if UNITY_ONLY_SWITCH
            return NNAccountSetup.NsaId;
#endif
                return default(nn.account.NetworkServiceAccountId);
            }
        }

        public bool isSucces
        {
            get
            {
#if UNITY_ONLY_SWITCH
            return NNAccountSetup.IsSuccess;
#endif
                return true;
            }
        }

        public bool GetNsaId()
        {
#if UNITY_ONLY_SWITCH
            return NNAccountSetup.GetNsaId();
#endif
            return true;
        }


        public void OpenUserAuto()
        {
#if UNITY_ONLY_SWITCH
            NNAccountSetup.OpenUserAuto();
#endif
        }
        public void OpenUserWithShowUserSelector()
        {
#if UNITY_ONLY_SWITCH
            NNAccountSetup.OpenUserWithShowUserSelector();
#endif
        }
        public void CloseUser()
        {
#if UNITY_ONLY_SWITCH
            NNAccountSetup.CloseUser();
#endif
        }

#endif
    }
}