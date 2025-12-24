using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ResearchTCG
{
    /// <summary>
    /// Minimal, research-oriented TCG with Cost System:
    /// - Click a card image to play it.
    /// - Discard a card to gain mana.
    /// - AI selects a card.
    /// - Higher attack wins the turn.
    /// - Logs each decision with reaction time.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // --- Game Phase ---
        public enum Phase
        {
            CardSelection,  // カード選択フェーズ
            ManaGain,       // マナ獲得フェーズ
            Battle          // バトルフェーズ
        }

        // --- Configurable parameters ---
        [Header("Game Config")]
        public int deckSize = 20;
        public int initialHandSize = 5;
        public float decisionTimeLimit = 0f; // seconds; 0 => no limit
        public string participantId = "P001"; // Change per participant as needed
        public int initialMana = 0; // 初期マナ（0に変更 - 駆け引き重視）

        // --- Runtime state ---
        private List<Card> allCards;
        private List<Card> playerDeck, aiDeck;
        private List<Card> playerHand, aiHand;
        private int playerScore = 0, aiScore = 0;
        private int playerMana = 0, aiMana = 0;
        private int turnNumber = 0;
        private bool waitingForPlayer = false;
        private float turnStartTime = 0f;
        private Phase currentPhase = Phase.CardSelection;
        private Card selectedCardToPlay = null; // 選択されたカード（マナ獲得フェーズで使用）
        private int playerBoostAmount = 0; // ブースト量（次のカードに適用）
        private bool waitingForManaAction = false; // マナアクション選択待ち

        // --- UI ---
        private Canvas canvas;
        private RectTransform headerPanel, phasePanel, centerPanel, handPanel;
        private Text headerText, infoText, playerLabel, aiLabel, resultText, phaseText, manaText;
        private Image playerPlayedImage, aiPlayedImage;
        private GameObject resultPanel;
        private GameObject manaActionPanel; // マナアクション選択パネル
        private Button boostButton, mulliganButton, skipButton;
        private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        // --- Logging ---
        //private DataLogger logger;

        // --- Util ---
        private System.Random rng = new System.Random();

        void Start()
        {
            EnsureCamera();
            EnsureEventSystem();
            BuildUI();
            LoadData();
            StartNewGame();
        }

        void Update()
        {
            if (waitingForPlayer && decisionTimeLimit > 0f && currentPhase == Phase.CardSelection)
            {
                float elapsed = Time.realtimeSinceStartup - turnStartTime;
                float remain = Mathf.Max(0f, decisionTimeLimit - elapsed);

                if (remain <= 0f)
                {
                    // Time out => auto-pick a random playable card
                    for (int i = 0; i < playerHand.Count; i++)
                    {
                        if (playerMana >= playerHand[i].cost)
                        {
                            HandlePlayerChoice(i, timedOut: true);
                            return;
                        }
                    }
                    // No playable cards, skip turn
                    Debug.LogWarning("タイムアウト：出せるカードがありません");
                }
            }
        }

        // --- Setup / Data ---

        void EnsureCamera()
        {
            if (Camera.main == null)
            {
                var camGO = new GameObject("Main Camera");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
                camGO.tag = "MainCamera";
            }
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }

        void LoadData()
        {
            // Load card list from Resources/card_data.json
            var json = Resources.Load<TextAsset>("card_data");

            if (json == null)
            {
                Debug.LogError("Missing Resources/card_data.json");
                allCards = new List<Card>();
            }
            else
            {
                var list = JsonUtility.FromJson<CardList>(json.text);
                allCards = list.cards.ToList();
            }

            //logger = new DataLogger(); // default path & file
        }

        // --- UI construction ---
        void BuildUI()
        {
            // Canvas
            var canvasGO = new GameObject("Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            // Header (top 12% height)
            headerPanel = CreatePanel(canvas.transform, "HeaderPanel", new Vector2(0, 0.88f), new Vector2(1, 1));
            headerText = CreateText(headerPanel, "HeaderText", "Research TCG — スコア", 32, TextAnchor.MiddleCenter);
            headerText.rectTransform.offsetMin = new Vector2(20, 10);
            headerText.rectTransform.offsetMax = new Vector2(-200, -10);

            // マナ表示（ヘッダー右側）
            var manaGO = new GameObject("ManaText");
            manaGO.transform.SetParent(headerPanel, false);
            manaText = manaGO.AddComponent<Text>();
            manaText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            manaText.fontSize = 64;
            manaText.fontStyle = FontStyle.Bold;
            manaText.alignment = TextAnchor.MiddleRight;
            manaText.color = new Color(0.3f, 0.9f, 0.9f); // シアン
            manaText.supportRichText = true;
            // 影追加
            var manaShadow = manaGO.AddComponent<UnityEngine.UI.Shadow>();
            manaShadow.effectColor = Color.black;
            manaShadow.effectDistance = new Vector2(3, -3);
            var manaRT = manaText.rectTransform;
            manaRT.anchorMin = new Vector2(0.65f, 0);
            manaRT.anchorMax = new Vector2(1, 1);
            manaRT.offsetMin = new Vector2(0, 5);
            manaRT.offsetMax = new Vector2(-30, -5);
            manaText.text = "マナ: 0";

            // フェーズ表示パネル（ヘッダーの下、8% height）
            phasePanel = CreatePanel(canvas.transform, "PhasePanel", new Vector2(0, 0.80f), new Vector2(1, 0.88f));
            var phasePanelImg = phasePanel.GetComponent<Image>();
            phasePanelImg.color = new Color(0.2f, 0.5f, 0.8f, 0.85f); // 青（初期値）
            phaseText = CreateText(phasePanel, "PhaseText", "カード選択フェーズ", 60, TextAnchor.MiddleCenter);
            phaseText.fontStyle = FontStyle.Bold;
            phaseText.color = Color.white;
            phaseText.rectTransform.offsetMin = new Vector2(10, 5);
            phaseText.rectTransform.offsetMax = new Vector2(-10, -5);

            // Info (small text in phase panel)
            var infoGO = new GameObject("InfoText");
            infoGO.transform.SetParent(phasePanel, false);
            infoText = infoGO.AddComponent<Text>();
            infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            infoText.fontSize = 24;
            infoText.alignment = TextAnchor.LowerCenter;
            infoText.supportRichText = true;
            infoText.color = new Color(1f, 1f, 0.7f); // 薄い黄色
            var infoRT = infoText.rectTransform;
            infoRT.anchorMin = new Vector2(0, 0);
            infoRT.anchorMax = new Vector2(1, 0.35f);
            infoRT.offsetMin = new Vector2(20, 5);
            infoRT.offsetMax = new Vector2(-20, -5);
            infoText.text = "";

            // Center (played cards, 45% height)
            centerPanel = CreatePanel(canvas.transform, "CenterPanel", new Vector2(0, 0.35f), new Vector2(1, 0.80f));
            centerPanel.GetComponent<Image>().color = new Color(0, 0, 0, 0); // 完全透明

            // Player area (left half - 左側に変更)
            var plArea = CreatePanel(centerPanel, "PlayerArea", new Vector2(0, 0), new Vector2(0.5f, 1));
            plArea.GetComponent<Image>().color = new Color(0, 0, 0, 0); // 完全透明
            playerLabel = CreateText(plArea, "PlayerLabel", "あなた", 32, TextAnchor.UpperCenter);
            playerLabel.color = new Color(0.5f, 0.7f, 1f); // 青文字
            var plImgGO = new GameObject("PlayerPlayedImage");
            plImgGO.transform.SetParent(canvas.transform, false); // canvasの直接の子にして最背面に
            playerPlayedImage = plImgGO.AddComponent<Image>();
            playerPlayedImage.transform.SetAsFirstSibling(); // 最背面に配置
            playerPlayedImage.preserveAspect = true; // アスペクト比維持
            playerPlayedImage.color = Color.clear;
            var plImgRT = playerPlayedImage.rectTransform;
            plImgRT.anchorMin = new Vector2(0.12f, 0.42f); // 左側に
            plImgRT.anchorMax = new Vector2(0.12f, 0.42f);
            plImgRT.pivot = new Vector2(0.5f, 0.5f);
            plImgRT.sizeDelta = new Vector2(320, 450);
            plImgRT.anchoredPosition = new Vector2(0, 0);

            // AI area (right half - 右側に変更)
            var aiArea = CreatePanel(centerPanel, "AIArea", new Vector2(0.5f, 0), new Vector2(1, 1));
            aiArea.GetComponent<Image>().color = new Color(0, 0, 0, 0); // 完全透明
            aiLabel = CreateText(aiArea, "AILabel", "敵 AI", 32, TextAnchor.UpperCenter);
            aiLabel.color = new Color(1f, 0.4f, 0.4f); // 赤文字
            var aiImgGO = new GameObject("AIPlayedImage");
            aiImgGO.transform.SetParent(canvas.transform, false); // canvasの直接の子にして最背面に
            aiPlayedImage = aiImgGO.AddComponent<Image>();
            aiPlayedImage.transform.SetAsFirstSibling(); // 最背面に配置
            aiPlayedImage.preserveAspect = true; // アスペクト比維持
            var aiImgRT = aiPlayedImage.rectTransform;
            aiImgRT.anchorMin = new Vector2(0.88f, 0.42f); // 右側に
            aiImgRT.anchorMax = new Vector2(0.88f, 0.42f);
            aiImgRT.pivot = new Vector2(0.5f, 0.5f);
            aiImgRT.sizeDelta = new Vector2(320, 450);
            aiImgRT.anchoredPosition = new Vector2(0, 0);

            // Hand (bottom 35% height)
            handPanel = CreatePanel(canvas.transform, "HandPanel", new Vector2(0, 0), new Vector2(1, 0.35f));
            var grid = handPanel.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(180, 260);
            grid.spacing = new Vector2(15, 15);
            grid.padding = new RectOffset(20, 20, 10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            grid.constraintCount = 1;

            // 勝敗結果パネル（中央全画面、初期非表示）
            resultPanel = new GameObject("ResultPanel");
            resultPanel.transform.SetParent(canvas.transform, false);
            var resultPanelRT = resultPanel.AddComponent<RectTransform>();
            resultPanelRT.anchorMin = Vector2.zero;
            resultPanelRT.anchorMax = Vector2.one;
            resultPanelRT.offsetMin = Vector2.zero;
            resultPanelRT.offsetMax = Vector2.zero;
            var resultBg = resultPanel.AddComponent<Image>();
            resultBg.color = new Color(0, 0, 0, 0.7f); // 半透明黒背景
            resultPanel.SetActive(false);

            // 勝敗テキスト
            var resultTextGO = new GameObject("ResultText");
            resultTextGO.transform.SetParent(resultPanel.transform, false);
            resultText = resultTextGO.AddComponent<Text>();
            resultText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            resultText.fontSize = 120;
            resultText.fontStyle = FontStyle.Bold;
            resultText.alignment = TextAnchor.MiddleCenter;
            resultText.supportRichText = true;
            resultText.color = Color.white;
            var resultTextRT = resultText.rectTransform;
            resultTextRT.anchorMin = Vector2.zero;
            resultTextRT.anchorMax = Vector2.one;
            resultTextRT.offsetMin = Vector2.zero;
            resultTextRT.offsetMax = Vector2.zero;

            // マナアクション選択パネル（中央上部、初期非表示）
            manaActionPanel = new GameObject("ManaActionPanel");
            manaActionPanel.transform.SetParent(canvas.transform, false);
            var manaActionRT = manaActionPanel.AddComponent<RectTransform>();
            manaActionRT.anchorMin = new Vector2(0.25f, 0.65f);
            manaActionRT.anchorMax = new Vector2(0.75f, 0.9f);
            manaActionRT.offsetMin = Vector2.zero;
            manaActionRT.offsetMax = Vector2.zero;
            var manaActionBg = manaActionPanel.AddComponent<Image>();
            manaActionBg.color = new Color(0.2f, 0.2f, 0.3f, 0.95f);
            manaActionPanel.SetActive(false);

            // タイトル
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(manaActionPanel.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 32;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.text = "マナアクション";
            titleText.color = Color.yellow;
            var titleRT = titleText.rectTransform;
            titleRT.anchorMin = new Vector2(0, 0.65f);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;

            // ボタン配置エリア
            var buttonAreaGO = new GameObject("ButtonArea");
            buttonAreaGO.transform.SetParent(manaActionPanel.transform, false);
            var buttonAreaRT = buttonAreaGO.AddComponent<RectTransform>();
            buttonAreaRT.anchorMin = new Vector2(0, 0);
            buttonAreaRT.anchorMax = new Vector2(1, 0.65f);
            buttonAreaRT.offsetMin = Vector2.zero;
            buttonAreaRT.offsetMax = Vector2.zero;

            // ブーストボタン
            boostButton = CreateManaActionButton(buttonAreaGO.transform, "ブースト", new Vector2(0.05f, 0.3f), new Vector2(0.3f, 0.9f), () => HandleManaAction_Boost());

            // マリガンボタン
            mulliganButton = CreateManaActionButton(buttonAreaGO.transform, "マリガン", new Vector2(0.37f, 0.3f), new Vector2(0.63f, 0.9f), () => HandleManaAction_Mulligan());

            // スキップボタン
            skipButton = CreateManaActionButton(buttonAreaGO.transform, "スキップ", new Vector2(0.7f, 0.3f), new Vector2(0.95f, 0.9f), () => HandleManaAction_Skip());
        }

        Button CreateManaActionButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction action)
        {
            var btnGO = new GameObject(label + "Button");
            btnGO.transform.SetParent(parent, false);
            var btnRT = btnGO.AddComponent<RectTransform>();
            btnRT.anchorMin = anchorMin;
            btnRT.anchorMax = anchorMax;
            btnRT.offsetMin = Vector2.zero;
            btnRT.offsetMax = Vector2.zero;

            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = new Color(0.3f, 0.5f, 0.8f);

            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.onClick.AddListener(action);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            text.color = Color.white;
            var textRT = text.rectTransform;
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            return btn;
        }

        RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 0.03f);
            return rt;
        }

        Text CreateText(RectTransform parent, string name, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = anchor;
            t.text = text;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(10, 10);
            rt.offsetMax = new Vector2(-10, -10);
            return t;
        }

        Sprite LoadSprite(string pathNoExt)
        {
            if (string.IsNullOrEmpty(pathNoExt)) return null;
            if (spriteCache.TryGetValue(pathNoExt, out var sp)) return sp;
            var s = Resources.Load<Sprite>(pathNoExt);
            if (s == null)
            {
                Debug.LogWarning($"Sprite not found at Resources/{pathNoExt}.png");
                return null; // backSpriteではなくnullを返す
            }
            spriteCache[pathNoExt] = s;
            return s;
        }

        // --- Game Flow ---

        void StartNewGame()
        {
            playerScore = 0; aiScore = 0; turnNumber = 0;
            playerMana = initialMana; aiMana = initialMana;
            playerDeck = GenerateDeck(deckSize);
            aiDeck = GenerateDeck(deckSize);
            Shuffle(playerDeck);
            Shuffle(aiDeck);
            playerHand = new List<Card>();
            aiHand = new List<Card>();
            Draw(playerDeck, playerHand, initialHandSize);
            Draw(aiDeck, aiHand, initialHandSize);
            selectedCardToPlay = null;
            UpdateHeader();
            UpdateManaDisplay();
            ResetCenterCards();

            Debug.Log("=== ゲーム開始 ===");
            Debug.Log($"デッキサイズ: {deckSize}, 初期手札: {initialHandSize}, 初期マナ: {initialMana}");

            // 初回ターン：手札2枚以上ならマナ獲得フェーズから、1枚以下ならカード選択フェーズから
            if (playerHand.Count > 1)
            {
                currentPhase = Phase.ManaGain;
            }
            else
            {
                currentPhase = Phase.CardSelection;
            }
            UpdatePhaseDisplay();
            RenderHand();
            waitingForPlayer = true;
            turnStartTime = Time.realtimeSinceStartup;
        }

        List<Card> GenerateDeck(int size)
        {
            var deck = new List<Card>();
            if (allCards == null || allCards.Count == 0)
            {
                Debug.LogError("No card data loaded.");
                return deck;
            }
            for (int i = 0; i < size; i++)
            {
                var c = allCards[UnityEngine.Random.Range(0, allCards.Count)];
                // shallow copy to avoid reference aliasing
                deck.Add(new Card
                {
                    id = c.id, name = c.name, attack = c.attack, cost = c.cost, type = c.type, sprite = c.sprite
                });
            }
            return deck;
        }

        void Shuffle<T>(IList<T> list)
        {
            // Fisher-Yates
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        void Draw(List<Card> deck, List<Card> hand, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (deck.Count == 0) break;
                var c = deck[0];
                deck.RemoveAt(0);
                hand.Add(c);
            }
        }

        void ResetCenterCards()
        {
            if (aiPlayedImage != null)
            {
                aiPlayedImage.sprite = null;
                //aiPlayedImage.color = Color.white;
                aiPlayedImage.color = Color.clear;
            }
            if (playerPlayedImage != null)
            {
                playerPlayedImage.sprite = null;
                //playerPlayedImage.color = Color.white;
                playerPlayedImage.color = Color.clear;
            }
            if (aiLabel != null) aiLabel.text = "敵 AI";
            if (playerLabel != null) playerLabel.text = "あなた";
        }

        System.Collections.IEnumerator BattleSequence(Card playerCard, Card aiCard, float reactionMs, bool timedOut)
        {
            infoText.text = "<size=24><color=yellow>★ カードバトル！ ★</color></size>";

            // カード衝突演出
            yield return StartCoroutine(CardCollisionSequence(playerCard, aiCard));

            // 少し待つ（対決の緊張感）
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // 勝敗判定
            string result;
            string resultDisplayText;
            Color resultColor;
            if (playerCard.attack > aiCard.attack)
            {
                playerScore++;
                result = "Win";
                resultDisplayText = "勝利！";
                resultColor = new Color(0.2f, 1f, 0.2f); // 明るい緑
                Debug.Log($">>> 結果: プレイヤーの勝利！ ({playerCard.attack} > {aiCard.attack})");
            }
            else if (playerCard.attack < aiCard.attack)
            {
                aiScore++;
                result = "Lose";
                resultDisplayText = "敗北...";
                resultColor = new Color(1f, 0.2f, 0.2f); // 明るい赤
                Debug.Log($">>> 結果: AIの勝利 ({playerCard.attack} < {aiCard.attack})");
            }
            else
            {
                // 攻撃力が同じ → 属性相性で判定
                int typeAdvantage = GetTypeAdvantage(playerCard.type, aiCard.type);
                if (typeAdvantage > 0)
                {
                    playerScore++;
                    result = "Win";
                    resultDisplayText = "勝利！\n<size=60>（属性有利）</size>";
                    resultColor = new Color(0.2f, 1f, 0.2f);
                    Debug.Log($">>> 結果: プレイヤーの勝利（属性有利）！ {playerCard.type} > {aiCard.type}");
                }
                else if (typeAdvantage < 0)
                {
                    aiScore++;
                    result = "Lose";
                    resultDisplayText = "敗北...\n<size=60>（属性不利）</size>";
                    resultColor = new Color(1f, 0.2f, 0.2f);
                    Debug.Log($">>> 結果: AIの勝利（属性有利）！ {aiCard.type} > {playerCard.type}");
                }
                else
                {
                    result = "Draw";
                    resultDisplayText = "引き分け";
                    resultColor = new Color(1f, 1f, 0.3f);
                    Debug.Log($">>> 結果: 完全引き分け (攻撃力={playerCard.attack}, 同属性={playerCard.type})");
                }
            }

            turnNumber++;
            UpdateHeader();

            // Log
            //logger.Append(participantId, turnNumber, playerCard, aiCard, result,
                          //playerScore, aiScore, reactionMs, playerHand.Count, timedOut);

            Debug.Log($"スコア - プレイヤー: {playerScore}, AI: {aiScore}");

            // 勝敗演出（カードへのエフェクト）
            bool playerWon = result == "Win";
            bool isDraw = result == "Draw";
            if (!isDraw)
            {
                yield return StartCoroutine(VictoryDefeatEffect(playerWon, playerCard, aiCard));
            }

            // 勝敗を画面中央に大きく表示（強化版）
            resultText.text = resultDisplayText;
            resultText.color = resultColor;
            resultPanel.SetActive(true);
            resultText.transform.localScale = Vector3.zero;

            // 画面フラッシュ（勝敗に応じた色）
            GameObject resultFlash = CreateScreenFlash();
            var flashImg = resultFlash.GetComponent<Image>();
            Color flashColor = playerWon ? new Color(0.3f, 1f, 0.3f) : // 勝利=緑
                               isDraw ? new Color(1f, 1f, 0.3f) : // 引き分け=黄色
                               new Color(1f, 0.3f, 0.3f); // 敗北=赤

            // 画面揺れ（勝敗時）
            Vector3 originalCanvasPos = canvas.transform.position;

            // 勝敗テキストをド派手に拡大表示 + 画面揺れ + フラッシュ
            float duration = 0.6f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float scale = Mathf.Lerp(0f, 2.0f, t);
                resultText.transform.localScale = Vector3.one * scale;

                // 画面揺れ（勝敗時のみ）
                if (!isDraw)
                {
                    float shake = Mathf.Sin(t * 40f) * (1f - t) * 15f;
                    canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);
                }

                // フラッシュ
                float alpha = t < 0.2f ? Mathf.Lerp(0f, 0.6f, t / 0.2f) : Mathf.Lerp(0.6f, 0f, (t - 0.2f) / 0.8f);
                flashImg.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);

                yield return null;
            }

            // 画面を元に戻す
            canvas.transform.position = originalCanvasPos;
            Destroy(resultFlash);

            // 少し縮んで通常サイズに（バウンス）
            elapsed = 0f;
            duration = 0.3f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float scale = Mathf.Lerp(2.0f, 1.2f, t);
                resultText.transform.localScale = Vector3.one * scale;
                yield return null;
            }

            // パーティクル風エフェクト（勝利時のみ）
            if (playerWon)
            {
                for (int i = 0; i < 20; i++)
                {
                    CreateVictoryParticle();
                }
            }

            // 2秒表示
            yield return new UnityEngine.WaitForSeconds(2.0f);

            // フェードアウト
            elapsed = 0f;
            duration = 0.3f;
            Color startColor = resultText.color;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                resultText.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(1f, 0f, t));
                yield return null;
            }

            resultPanel.SetActive(false);

            // Draw next cards (2枚: カードプレイで1枚 + 捨て札で1枚 = 合計2枚減ったため)
            Draw(playerDeck, playerHand, 2);
            Draw(aiDeck, aiHand, 2);

            // Next turn or end
            if (playerHand.Count == 0 || aiHand.Count == 0)
            {
                Debug.Log("=== ゲーム終了 ===");
                Debug.Log($"最終スコア - プレイヤー: {playerScore}, AI: {aiScore}");
                //Debug.Log($"ログ: {logger.GetLogPath()}");

                // 超派手な最終結果画面を表示
                yield return StartCoroutine(ShowGameOverScreen());
            }
            else
            {
                yield return new UnityEngine.WaitForSeconds(0.3f);
                BeginNextTurn();
            }
        }

        /// <summary>
        /// カード戦闘演出（リンバス風バチバチ）
        /// </summary>
        System.Collections.IEnumerator CardCollisionSequence(Card playerCard, Card aiCard)
        {
            // 初期位置を保存
            Vector3 playerStartPos = playerPlayedImage.rectTransform.position;
            Vector3 aiStartPos = aiPlayedImage.rectTransform.position;
            Vector3 centerPos = (playerStartPos + aiStartPos) / 2f;

            // カードの初期化
            playerPlayedImage.sprite = LoadSprite(playerCard.sprite);
            aiPlayedImage.sprite = LoadSprite(aiCard.sprite);
            playerPlayedImage.color = GetCardColor(playerCard.type);
            aiPlayedImage.color = GetCardColor(aiCard.type);
            playerLabel.text = $"{playerCard.name}\n<size=24><color=yellow>ATK {playerCard.attack}</color></size>";
            aiLabel.text = $"{aiCard.name}\n<size=24><color=yellow>ATK {aiCard.attack}</color></size>";

            playerPlayedImage.transform.localScale = Vector3.zero;
            aiPlayedImage.transform.localScale = Vector3.zero;

            // レアリティ判定（攻撃力7以上を最高レア扱い）
            bool isPlayerUltraRare = playerCard.attack >= 7;
            bool isAiUltraRare = aiCard.attack >= 7;

            // フェーズ1: カード出現（画面端から）
            Vector3 playerOffScreen = playerStartPos + Vector3.left * 1500f; // 左から高速登場
            Vector3 aiOffScreen = aiStartPos + Vector3.right * 1500f; // 右から高速登場
            playerPlayedImage.rectTransform.position = playerOffScreen;
            aiPlayedImage.rectTransform.position = aiOffScreen;

            float duration = 0.6f;
            float elapsed = 0f;

            // 最高レアの場合は画面全体フラッシュ
            GameObject flashOverlay = null;
            if (isPlayerUltraRare || isAiUltraRare)
            {
                flashOverlay = CreateScreenFlash();
            }

            // 画面端から登場
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic

                playerPlayedImage.rectTransform.position = Vector3.Lerp(playerOffScreen, playerStartPos, eased);
                aiPlayedImage.rectTransform.position = Vector3.Lerp(aiOffScreen, aiStartPos, eased);

                float scale = Mathf.Lerp(0f, 1.5f, eased);
                playerPlayedImage.transform.localScale = Vector3.one * scale;
                aiPlayedImage.transform.localScale = Vector3.one * scale;

                // 最高レアのフラッシュエフェクト
                if (flashOverlay != null)
                {
                    var img = flashOverlay.GetComponent<Image>();
                    float alpha = t < 0.3f ? Mathf.Lerp(0f, 0.8f, t / 0.3f) : Mathf.Lerp(0.8f, 0f, (t - 0.3f) / 0.7f);
                    Color flashColor = isPlayerUltraRare && isAiUltraRare ? Color.white :
                                       isPlayerUltraRare ? new Color(0.3f, 0.8f, 1f) :
                                       new Color(1f, 0.3f, 0.3f);
                    img.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
                }

                yield return null;
            }

            if (flashOverlay != null)
            {
                Destroy(flashOverlay);
            }

            // 通常サイズに
            yield return new UnityEngine.WaitForSeconds(0.15f);
            playerPlayedImage.transform.localScale = Vector3.one;
            aiPlayedImage.transform.localScale = Vector3.one;

            // 最高レアの場合はオーラエフェクト
            GameObject playerAura = null;
            GameObject aiAura = null;
            if (isPlayerUltraRare)
            {
                playerAura = CreateAuraEffect(playerPlayedImage.rectTransform, GetCardColor(playerCard.type));
            }
            if (isAiUltraRare)
            {
                aiAura = CreateAuraEffect(aiPlayedImage.rectTransform, GetCardColor(aiCard.type));
            }

            yield return new UnityEngine.WaitForSeconds(0.5f);

            if (playerAura != null) Destroy(playerAura);
            if (aiAura != null) Destroy(aiAura);

            // フェーズ2: カード中央へ突進（少しだけ近づく、完全に重ならない）
            Vector3 playerBattlePos = Vector3.Lerp(playerStartPos, centerPos, 0.7f); // 70%だけ近づく
            Vector3 aiBattlePos = Vector3.Lerp(aiStartPos, centerPos, 0.7f);

            duration = 0.4f;
            elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float eased = Mathf.Pow(t, 2f); // ease-in quad

                playerPlayedImage.rectTransform.position = Vector3.Lerp(playerStartPos, playerBattlePos, eased);
                aiPlayedImage.rectTransform.position = Vector3.Lerp(aiStartPos, aiBattlePos, eased);

                yield return null;
            }

            // フェーズ3: バトル！（バチバチ演出 + 攻撃力の押し合い）
            yield return StartCoroutine(BattleClash(playerBattlePos, aiBattlePos, centerPos, playerCard, aiCard));

            // フェーズ4: カードを元の位置に戻す
            duration = 0.5f;
            elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float eased = 1f - Mathf.Pow(1f - t, 2f); // ease-out

                playerPlayedImage.rectTransform.position = Vector3.Lerp(playerBattlePos, playerStartPos, eased);
                aiPlayedImage.rectTransform.position = Vector3.Lerp(aiBattlePos, aiStartPos, eased);

                yield return null;
            }
        }

        /// <summary>
        /// バトル激突演出（リンバス風：カード間でバチバチ + 攻撃力押し合い + カード個別演出）
        /// </summary>
        System.Collections.IEnumerator BattleClash(Vector3 playerPos, Vector3 aiPos, Vector3 centerPos, Card playerCard, Card aiCard)
        {
            // 攻撃力比較（勝敗判定は後でするが、ここでは押し合い用）
            int playerAtk = playerCard.attack;
            int aiAtk = aiCard.attack;
            bool playerStronger = playerAtk > aiAtk;
            bool tied = playerAtk == aiAtk;

            // カード個別演出を先に実行（目立たせる）
            yield return StartCoroutine(CardSpecialEffect(playerCard, playerPos, centerPos, true));
            yield return StartCoroutine(CardSpecialEffect(aiCard, aiPos, centerPos, false));

            // 少し間を置く
            yield return new UnityEngine.WaitForSeconds(0.3f);

            // バトル時間
            float battleDuration = 1.2f;
            float elapsed = 0f;

            // 攻撃力数値表示（中央に大きく）
            var atkDisplayGO = new GameObject("AttackDisplay");
            atkDisplayGO.transform.SetParent(canvas.transform, false);
            var atkText = atkDisplayGO.AddComponent<Text>();
            atkText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            atkText.fontSize = 80;
            atkText.fontStyle = FontStyle.Bold;
            atkText.alignment = TextAnchor.MiddleCenter;
            atkText.supportRichText = true;
            atkText.text = $"<color=cyan>{playerAtk}</color> vs <color=red>{aiAtk}</color>";

            var atkRT = atkText.rectTransform;
            atkRT.anchorMin = new Vector2(0.5f, 0.5f);
            atkRT.anchorMax = new Vector2(0.5f, 0.5f);
            atkRT.pivot = new Vector2(0.5f, 0.5f);
            atkRT.sizeDelta = new Vector2(600, 150);
            atkRT.position = centerPos;

            var atkShadow = atkDisplayGO.AddComponent<Outline>();
            atkShadow.effectColor = Color.black;
            atkShadow.effectDistance = new Vector2(5, -5);

            // 稲妻エフェクト用のパーティクルリスト
            var lightningParticles = new List<(RectTransform rt, float lifetime, Vector3 startPos, Vector3 endPos)>();

            while (elapsed < battleDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / battleDuration;
                float dt = Time.deltaTime;

                // カードが小刻みに振動（押し合い）
                float shakeAmount = Mathf.Sin(elapsed * 50f) * 8f * (1f - t); // 徐々に弱まる
                Vector3 playerShake = playerPos + new Vector3(shakeAmount, UnityEngine.Random.Range(-3f, 3f), 0);
                Vector3 aiShake = aiPos + new Vector3(-shakeAmount, UnityEngine.Random.Range(-3f, 3f), 0);

                // 攻撃力差による押し合い（強い方が相手を押す）
                if (!tied)
                {
                    float pushAmount = Mathf.Sin(t * Mathf.PI) * 30f; // 山なり
                    if (playerStronger)
                    {
                        aiShake += Vector3.left * pushAmount * 0.5f;
                    }
                    else
                    {
                        playerShake += Vector3.right * pushAmount * 0.5f;
                    }
                }

                playerPlayedImage.rectTransform.position = playerShake;
                aiPlayedImage.rectTransform.position = aiShake;

                // カードのスケールパルス
                float pulse = 1f + Mathf.Sin(elapsed * 20f) * 0.05f;
                playerPlayedImage.transform.localScale = Vector3.one * pulse;
                aiPlayedImage.transform.localScale = Vector3.one * pulse;

                // 攻撃力表示のパルス
                float atkPulse = 1f + Mathf.Sin(elapsed * 15f) * 0.1f;
                atkRT.localScale = Vector3.one * atkPulse;

                // 稲妻エフェクト生成（ランダムに）
                if (UnityEngine.Random.value < 0.3f) // 30%の確率で毎フレーム
                {
                    var lightningGO = new GameObject("Lightning");
                    lightningGO.transform.SetParent(canvas.transform, false);
                    var lightningImg = lightningGO.AddComponent<Image>();
                    lightningImg.color = new Color(1f, 1f, 0.5f, 1f); // 黄色稲妻

                    var lightningRT = lightningImg.rectTransform;
                    lightningRT.anchorMin = new Vector2(0.5f, 0.5f);
                    lightningRT.anchorMax = new Vector2(0.5f, 0.5f);
                    lightningRT.pivot = new Vector2(0.5f, 0.5f);

                    // カード間のランダムな位置
                    Vector3 startPos = playerPos + new Vector3(UnityEngine.Random.Range(-50f, 50f), UnityEngine.Random.Range(-100f, 100f), 0);
                    Vector3 endPos = aiPos + new Vector3(UnityEngine.Random.Range(-50f, 50f), UnityEngine.Random.Range(-100f, 100f), 0);
                    Vector3 midPoint = (startPos + endPos) / 2f;

                    lightningRT.position = midPoint;

                    // 2点間の距離と角度を計算
                    float distance = Vector3.Distance(startPos, endPos);
                    float angle = Mathf.Atan2(endPos.y - startPos.y, endPos.x - startPos.x) * Mathf.Rad2Deg;

                    lightningRT.sizeDelta = new Vector2(distance, UnityEngine.Random.Range(3f, 8f)); // 幅はランダム
                    lightningRT.rotation = Quaternion.Euler(0, 0, angle);

                    lightningParticles.Add((lightningRT, 0.15f, startPos, endPos)); // 0.15秒で消える
                }

                // 火花パーティクル生成
                if (UnityEngine.Random.value < 0.4f)
                {
                    var sparkGO = new GameObject("Spark");
                    sparkGO.transform.SetParent(canvas.transform, false);
                    var sparkImg = sparkGO.AddComponent<Image>();

                    // 属性色をミックス
                    Color sparkColor = Color.Lerp(GetAttributeColor(playerCard.type), GetAttributeColor(aiCard.type), UnityEngine.Random.value);
                    sparkImg.color = sparkColor;

                    var sparkRT = sparkImg.rectTransform;
                    sparkRT.anchorMin = new Vector2(0.5f, 0.5f);
                    sparkRT.anchorMax = new Vector2(0.5f, 0.5f);
                    sparkRT.pivot = new Vector2(0.5f, 0.5f);
                    sparkRT.sizeDelta = new Vector2(UnityEngine.Random.Range(5f, 15f), UnityEngine.Random.Range(5f, 15f));
                    sparkRT.position = centerPos + new Vector3(UnityEngine.Random.Range(-100f, 100f), UnityEngine.Random.Range(-100f, 100f), 0);

                    Vector2 velocity = new Vector2(UnityEngine.Random.Range(-300f, 300f), UnityEngine.Random.Range(-300f, 300f));
                    lightningParticles.Add((sparkRT, 0.3f, sparkRT.position, sparkRT.position)); // velocityは別で管理が必要だが簡易版
                }

                // 稲妻・火花の寿命管理
                for (int i = lightningParticles.Count - 1; i >= 0; i--)
                {
                    var particle = lightningParticles[i];
                    float newLifetime = particle.lifetime - dt;

                    if (newLifetime <= 0)
                    {
                        Destroy(particle.rt.gameObject);
                        lightningParticles.RemoveAt(i);
                    }
                    else
                    {
                        // フェードアウト
                        var img = particle.rt.GetComponent<Image>();
                        if (img != null)
                        {
                            Color c = img.color;
                            c.a = newLifetime / 0.3f; // 元の寿命で割る
                            img.color = c;
                        }

                        lightningParticles[i] = (particle.rt, newLifetime, particle.startPos, particle.endPos);
                    }
                }

                yield return null;
            }

            // クリーンアップ
            foreach (var particle in lightningParticles)
            {
                Destroy(particle.rt.gameObject);
            }
            Destroy(atkDisplayGO);

            // 最後に衝撃波
            yield return StartCoroutine(CollisionImpact(centerPos, playerCard.type, aiCard.type));
        }

        /// <summary>
        /// 衝突衝撃波エフェクト + カメラシェイク + 属性別パーティクル
        /// </summary>
        System.Collections.IEnumerator CollisionImpact(Vector3 impactPos, string type1, string type2)
        {
            // カメラシェイク
            StartCoroutine(CameraShake(0.3f, 15f));

            // 衝撃波リング
            var shockwaveGO = new GameObject("Shockwave");
            shockwaveGO.transform.SetParent(canvas.transform, false);
            var shockwaveImg = shockwaveGO.AddComponent<Image>();
            shockwaveImg.color = new Color(1f, 1f, 1f, 0.8f);
            var shockwaveRT = shockwaveImg.rectTransform;
            shockwaveRT.position = impactPos;
            shockwaveRT.anchorMin = new Vector2(0.5f, 0.5f);
            shockwaveRT.anchorMax = new Vector2(0.5f, 0.5f);
            shockwaveRT.pivot = new Vector2(0.5f, 0.5f);
            shockwaveRT.sizeDelta = Vector2.zero;

            // リング拡大
            float duration = 0.5f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                float size = Mathf.Lerp(0f, 800f, t);
                shockwaveRT.sizeDelta = new Vector2(size, size);
                shockwaveImg.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.8f, 0f, t));

                yield return null;
            }
            Destroy(shockwaveGO);

            // 属性別パーティクルバースト
            yield return StartCoroutine(AttributeParticleBurst(impactPos, type1, type2));
        }

        /// <summary>
        /// カメラシェイク（Canvas全体を揺らす）
        /// </summary>
        System.Collections.IEnumerator CameraShake(float duration, float magnitude)
        {
            Vector3 originalPos = canvas.transform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = 1f - (elapsed / duration); // 徐々に弱まる

                float offsetX = UnityEngine.Random.Range(-1f, 1f) * magnitude * t;
                float offsetY = UnityEngine.Random.Range(-1f, 1f) * magnitude * t;

                canvas.transform.position = originalPos + new Vector3(offsetX, offsetY, 0);

                yield return null;
            }

            canvas.transform.position = originalPos;
        }

        /// <summary>
        /// 属性別パーティクルバースト
        /// </summary>
        System.Collections.IEnumerator AttributeParticleBurst(Vector3 center, string type1, string type2)
        {
            // 両方の属性をミックス
            List<string> types = new List<string> { type1, type2 };

            int particleCount = 60;
            var particles = new List<(RectTransform rt, Vector2 velocity, Color color, string symbol, bool isText)>();

            for (int i = 0; i < particleCount; i++)
            {
                var particleGO = new GameObject($"BurstParticle{i}");
                particleGO.transform.SetParent(canvas.transform, false);

                string type = types[i % 2];
                bool isText = (i % 3 == 0); // 3つに1つは文字
                RectTransform particleRT;

                if (isText)
                {
                    // 属性文字
                    var text = particleGO.AddComponent<Text>();
                    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    text.fontSize = 40;
                    text.fontStyle = FontStyle.Bold;
                    text.alignment = TextAnchor.MiddleCenter;
                    text.text = GetAttributeSymbol(type);
                    text.color = GetAttributeColor(type);

                    var outline = particleGO.AddComponent<Outline>();
                    outline.effectColor = Color.black;
                    outline.effectDistance = new Vector2(3, -3);

                    particleRT = text.rectTransform;
                    particleRT.sizeDelta = new Vector2(60, 60);
                }
                else
                {
                    // 光の粒
                    var img = particleGO.AddComponent<Image>();
                    img.color = GetAttributeColor(type);
                    particleRT = img.rectTransform;
                    particleRT.sizeDelta = new Vector2(15, 15);
                }

                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.position = center;

                // ランダムな方向に飛び散る
                float angle = (360f / particleCount) * i + UnityEngine.Random.Range(-15f, 15f);
                float speed = UnityEngine.Random.Range(300f, 800f);
                Vector2 velocity = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * speed,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * speed
                );

                particles.Add((particleRT, velocity, GetAttributeColor(type), GetAttributeSymbol(type), isText));
            }

            // パーティクルアニメーション
            float duration = 1.2f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                foreach (var particle in particles)
                {
                    // 物理演算（重力）
                    Vector2 gravity = new Vector2(0, -500f);
                    Vector2 newVelocity = particle.velocity + gravity * dt;
                    particle.rt.position += new Vector3(newVelocity.x * dt, newVelocity.y * dt, 0);

                    // フェード
                    float alpha = Mathf.Lerp(1f, 0f, t);
                    if (particle.isText)
                    {
                        var text = particle.rt.GetComponent<Text>();
                        Color c = text.color;
                        c.a = alpha;
                        text.color = c;
                    }
                    else
                    {
                        var img = particle.rt.GetComponent<Image>();
                        Color c = img.color;
                        c.a = alpha;
                        img.color = c;
                    }

                    // 回転
                    particle.rt.Rotate(0, 0, 500f * dt);
                }

                yield return null;
            }

            // クリーンアップ
            foreach (var particle in particles)
            {
                Destroy(particle.rt.gameObject);
            }
        }

        /// <summary>
        /// 勝敗演出（勝者：光る、敗者：砕ける）
        /// </summary>
        System.Collections.IEnumerator VictoryDefeatEffect(bool playerWon, Card playerCard, Card aiCard)
        {
            Image winnerImage = playerWon ? playerPlayedImage : aiPlayedImage;
            Image loserImage = playerWon ? aiPlayedImage : playerPlayedImage;
            Card winnerCard = playerWon ? playerCard : aiCard;

            // 勝者：光のパルス
            StartCoroutine(VictoryGlow(winnerImage, winnerCard.type));

            // 敗者：砕け散る
            yield return StartCoroutine(DefeatShatter(loserImage));
        }

        /// <summary>
        /// 勝利カード：光のパルス
        /// </summary>
        System.Collections.IEnumerator VictoryGlow(Image cardImage, string type)
        {
            Color originalColor = cardImage.color;
            Color glowColor = GetAttributeColor(type);
            glowColor.a = 1f;

            float duration = 0.8f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // パルス
                float pulse = Mathf.Sin(t * Mathf.PI * 6f);
                cardImage.color = Color.Lerp(originalColor, glowColor, pulse * 0.5f + 0.5f);

                // スケール
                float scale = 1f + pulse * 0.15f;
                cardImage.transform.localScale = Vector3.one * scale;

                yield return null;
            }

            cardImage.color = originalColor;
            cardImage.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// 敗北カード：砕け散る
        /// </summary>
        System.Collections.IEnumerator DefeatShatter(Image cardImage)
        {
            // カードを複数の破片に分割（シンプル版：9分割）
            Vector3 cardPos = cardImage.rectTransform.position;
            Vector2 cardSize = cardImage.rectTransform.sizeDelta;
            Sprite cardSprite = cardImage.sprite;
            Color cardColor = cardImage.color;

            var shards = new List<(RectTransform rt, Vector2 velocity)>();

            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    var shardGO = new GameObject($"Shard_{x}_{y}");
                    shardGO.transform.SetParent(canvas.transform, false);
                    var shardImg = shardGO.AddComponent<Image>();
                    shardImg.sprite = cardSprite;
                    shardImg.color = cardColor;
                    var shardRT = shardImg.rectTransform;
                    shardRT.anchorMin = new Vector2(0.5f, 0.5f);
                    shardRT.anchorMax = new Vector2(0.5f, 0.5f);
                    shardRT.pivot = new Vector2(0.5f, 0.5f);
                    shardRT.sizeDelta = cardSize / 3f;

                    float offsetX = (x - 1) * (cardSize.x / 3f);
                    float offsetY = (y - 1) * (cardSize.y / 3f);
                    shardRT.position = cardPos + new Vector3(offsetX, offsetY, 0);

                    // UVを調整して破片に見せる（簡易版：そのまま）
                    // ランダムな速度で飛び散る
                    Vector2 velocity = new Vector2(
                        UnityEngine.Random.Range(-200f, 200f),
                        UnityEngine.Random.Range(100f, 400f)
                    );

                    shards.Add((shardRT, velocity));
                }
            }

            // 元のカードを非表示
            cardImage.color = new Color(cardColor.r, cardColor.g, cardColor.b, 0);

            // 破片アニメーション
            float duration = 0.8f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                foreach (var shard in shards)
                {
                    // 重力
                    Vector2 gravity = new Vector2(0, -800f);
                    Vector2 newVelocity = shard.velocity + gravity * dt;
                    shard.rt.position += new Vector3(newVelocity.x * dt, newVelocity.y * dt, 0);

                    // 回転
                    shard.rt.Rotate(0, 0, UnityEngine.Random.Range(-500f, 500f) * dt);

                    // フェード
                    var img = shard.rt.GetComponent<Image>();
                    Color c = img.color;
                    c.a = Mathf.Lerp(1f, 0f, t);
                    img.color = c;
                }

                yield return null;
            }

            // クリーンアップ
            foreach (var shard in shards)
            {
                Destroy(shard.rt.gameObject);
            }

            // カードを元に戻す（次のターン用）
            cardImage.color = cardColor;
        }

        /// <summary>
        /// 属性カラー取得
        /// </summary>
        Color GetAttributeColor(string type)
        {
            return type.ToLower() switch
            {
                "fire" => new Color(1f, 0.3f, 0f, 1f),      // オレンジ炎
                "water" => new Color(0.2f, 0.6f, 1f, 1f),   // 青水
                "nature" => new Color(0.3f, 1f, 0.3f, 1f),  // 緑葉
                "earth" => new Color(0.6f, 0.4f, 0.2f, 1f), // 茶土
                "air" => new Color(0.8f, 1f, 1f, 1f),       // 白風
                _ => new Color(1f, 1f, 1f, 1f)
            };
        }

        /// <summary>
        /// 属性シンボル取得
        /// </summary>
        string GetAttributeSymbol(string type)
        {
            return type.ToLower() switch
            {
                "fire" => "炎",
                "water" => "水",
                "nature" => "草",
                "earth" => "土",
                "air" => "風",
                _ => "?"
            };
        }

        /// <summary>
        /// カード個別専用演出（全25種類）
        /// </summary>
        System.Collections.IEnumerator CardSpecialEffect(Card card, Vector3 cardPos, Vector3 targetPos, bool isPlayer)
        {
            // カード名を大きく表示（演出強調）
            var nameGO = new GameObject("CardNameDisplay");
            nameGO.transform.SetParent(canvas.transform, false);
            var nameText = nameGO.AddComponent<Text>();
            nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            nameText.fontSize = 100;
            nameText.fontStyle = FontStyle.Bold;
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.text = card.name;
            nameText.color = GetAttributeColor(card.type);
            nameText.supportRichText = true;

            var nameOutline = nameGO.AddComponent<Outline>();
            nameOutline.effectColor = Color.black;
            nameOutline.effectDistance = new Vector2(6, -6);

            var nameRT = nameText.rectTransform;
            nameRT.anchorMin = new Vector2(0.5f, 0.5f);
            nameRT.anchorMax = new Vector2(0.5f, 0.5f);
            nameRT.pivot = new Vector2(0.5f, 0.5f);
            nameRT.sizeDelta = new Vector2(800, 150);
            nameRT.position = cardPos + new Vector3(0, 200f, 0);

            // カード名アニメーション
            StartCoroutine(AnimateCardName(nameRT, nameText));

            // カードIDに応じて専用演出を実行
            switch (card.id)
            {
                // === Fire属性 ===
                case "F1": // Fire Wolf - 炎の遠吠え
                    yield return StartCoroutine(Effect_FireWolf(cardPos, targetPos, isPlayer));
                    break;
                case "F2": // Flame Imp - 小さな火の玉連射
                    yield return StartCoroutine(Effect_FlameImp(cardPos, targetPos, isPlayer));
                    break;
                case "F3": // Blaze Golem - 巨大爆炎
                    yield return StartCoroutine(Effect_BlazeGolem(cardPos, targetPos, isPlayer));
                    break;
                case "F4": // Lava Drake - 溶岩ブレス
                    yield return StartCoroutine(Effect_LavaDrake(cardPos, targetPos, isPlayer));
                    break;
                case "F5": // Cinder Fox - 炎の尾旋回
                    yield return StartCoroutine(Effect_CinderFox(cardPos, targetPos, isPlayer));
                    break;

                // === Water属性 ===
                case "W1": // Water Spirit - 水の渦巻き
                    yield return StartCoroutine(Effect_WaterSpirit(cardPos, targetPos, isPlayer));
                    break;
                case "W2": // Tide Caller - 波動砲
                    yield return StartCoroutine(Effect_TideCaller(cardPos, targetPos, isPlayer));
                    break;
                case "W3": // Ice Guardian - 氷の盾＋氷柱
                    yield return StartCoroutine(Effect_IceGuardian(cardPos, targetPos, isPlayer));
                    break;
                case "W4": // Stream Pixie - 水玉シャワー
                    yield return StartCoroutine(Effect_StreamPixie(cardPos, targetPos, isPlayer));
                    break;
                case "W5": // Aqua Serpent - 水蛇の噛みつき
                    yield return StartCoroutine(Effect_AquaSerpent(cardPos, targetPos, isPlayer));
                    break;

                // === Nature属性 ===
                case "N1": // Forest Elf - 葉っぱの舞
                    yield return StartCoroutine(Effect_ForestElf(cardPos, targetPos, isPlayer));
                    break;
                case "N2": // Vine Beast - ツタの鞭
                    yield return StartCoroutine(Effect_VineBeast(cardPos, targetPos, isPlayer));
                    break;
                case "N3": // Grove Guardian - 巨木召喚
                    yield return StartCoroutine(Effect_GroveGuardian(cardPos, targetPos, isPlayer));
                    break;
                case "N4": // Moss Turtle - 苔の盾
                    yield return StartCoroutine(Effect_MossTurtle(cardPos, targetPos, isPlayer));
                    break;
                case "N5": // Thorn Stalker - トゲ発射
                    yield return StartCoroutine(Effect_ThornStalker(cardPos, targetPos, isPlayer));
                    break;

                // === Earth属性 ===
                case "E1": // Rock Giant - 地割れパンチ
                    yield return StartCoroutine(Effect_RockGiant(cardPos, targetPos, isPlayer));
                    break;
                case "E2": // Clay Golem - 岩石落下
                    yield return StartCoroutine(Effect_ClayGolem(cardPos, targetPos, isPlayer));
                    break;
                case "E3": // Stone Boar - 突進
                    yield return StartCoroutine(Effect_StoneBoar(cardPos, targetPos, isPlayer));
                    break;
                case "E4": // Granite Rhino - 地震
                    yield return StartCoroutine(Effect_GraniteRhino(cardPos, targetPos, isPlayer));
                    break;
                case "E5": // Pebble Sprite - 小石投げ
                    yield return StartCoroutine(Effect_PebbleSprite(cardPos, targetPos, isPlayer));
                    break;

                // === Air属性 ===
                case "A1": // Wind Hawk - 風の刃
                    yield return StartCoroutine(Effect_WindHawk(cardPos, targetPos, isPlayer));
                    break;
                case "A2": // Gust Fox - 竜巻
                    yield return StartCoroutine(Effect_GustFox(cardPos, targetPos, isPlayer));
                    break;
                case "A3": // Sky Golem - 暴風
                    yield return StartCoroutine(Effect_SkyGolem(cardPos, targetPos, isPlayer));
                    break;
                case "A4": // Whirl Pixie - 小さな旋風
                    yield return StartCoroutine(Effect_WhirlPixie(cardPos, targetPos, isPlayer));
                    break;
                case "A5": // Storm Drake - 雷雲＋稲妻
                    yield return StartCoroutine(Effect_StormDrake(cardPos, targetPos, isPlayer));
                    break;

                default:
                    // デフォルト演出なし
                    yield break;
            }
        }

        /// <summary>
        /// カード名表示アニメーション
        /// </summary>
        System.Collections.IEnumerator AnimateCardName(RectTransform rt, Text text)
        {
            rt.localScale = Vector3.zero;
            float duration = 0.5f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // バーンと拡大
                if (t < 0.3f)
                {
                    float t1 = t / 0.3f;
                    rt.localScale = Vector3.one * Mathf.Lerp(0f, 1.3f, t1);
                }
                else
                {
                    float t2 = (t - 0.3f) / 0.7f;
                    rt.localScale = Vector3.one * Mathf.Lerp(1.3f, 1.0f, t2);
                }

                yield return null;
            }

            // 1秒表示
            yield return new UnityEngine.WaitForSeconds(1.0f);

            // フェードアウト
            duration = 0.3f;
            elapsed = 0f;
            Color startColor = text.color;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                Color c = text.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                text.color = c;

                yield return null;
            }

            Destroy(rt.gameObject);
        }

        // ========== Fire属性演出 ==========

        System.Collections.IEnumerator Effect_FireWolf(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ（炎色）
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ
            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 20f;

            // 炎の遠吠え：カードから炎が円錐状に広がる（超強化版）
            int flameCount = 150; // 50→150に増量
            var flames = new List<(RectTransform rt, Vector2 velocity)>();

            for (int i = 0; i < flameCount; i++)
            {
                var flameGO = new GameObject($"Flame{i}");
                flameGO.transform.SetParent(canvas.transform, false);
                var flameImg = flameGO.AddComponent<Image>();
                flameImg.color = new Color(1f, UnityEngine.Random.Range(0.2f, 0.5f), 0f, 1f); // より濃い赤

                var flameRT = flameImg.rectTransform;
                flameRT.anchorMin = new Vector2(0.5f, 0.5f);
                flameRT.anchorMax = new Vector2(0.5f, 0.5f);
                flameRT.pivot = new Vector2(0.5f, 0.5f);
                flameRT.sizeDelta = new Vector2(UnityEngine.Random.Range(80f, 160f), UnityEngine.Random.Range(80f, 160f)); // 3倍サイズ
                flameRT.position = from;

                // 扇形に発射
                float direction = isPlayer ? 180f : 0f; // プレイヤーは左なので右向き(0→180)に修正
                float spread = UnityEngine.Random.Range(-45f, 45f); // より広く
                float angle = direction + spread;
                float speed = UnityEngine.Random.Range(600f, 1000f); // より速く

                Vector2 velocity = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * speed,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * speed
                );

                flames.Add((flameRT, velocity));
            }

            float duration = 1.0f; // 少し長く
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                // 画面揺れ
                float shake = Mathf.Sin(t * 40f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);

                // フラッシュ（炎色）
                float alpha = t < 0.2f ? Mathf.Lerp(0f, 0.6f, t / 0.2f) : Mathf.Lerp(0.6f, 0f, (t - 0.2f) / 0.8f);
                flashImg.color = new Color(1f, 0.3f, 0f, alpha);

                foreach (var flame in flames)
                {
                    // 移動
                    flame.rt.position += new Vector3(flame.velocity.x * dt, flame.velocity.y * dt, 0);

                    // フェード＋拡大
                    var img = flame.rt.GetComponent<Image>();
                    Color c = img.color;
                    c.a = Mathf.Lerp(1f, 0f, t);
                    img.color = c;

                    float scale = Mathf.Lerp(2f, 4f, t); // 3倍に拡大
                    flame.rt.localScale = Vector3.one * scale;

                    // 回転
                    flame.rt.Rotate(0, 0, 800f * dt);
                }

                yield return null;
            }

            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var flame in flames) Destroy(flame.rt.gameObject);
        }

        System.Collections.IEnumerator Effect_FlameImp(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ
            Vector3 originalCanvasPos = canvas.transform.position;

            // 小さな火の玉連射（超強化版）
            int shotCount = 45; // 15→45に3倍
            float shotDelay = 0.04f; // より速く

            for (int s = 0; s < shotCount; s++)
            {
                var fireballGO = new GameObject("Fireball");
                fireballGO.transform.SetParent(canvas.transform, false);
                var fireballImg = fireballGO.AddComponent<Image>();
                fireballImg.color = new Color(1f, UnityEngine.Random.Range(0.2f, 0.5f), 0f, 1f);

                var fireballRT = fireballImg.rectTransform;
                fireballRT.anchorMin = new Vector2(0.5f, 0.5f);
                fireballRT.anchorMax = new Vector2(0.5f, 0.5f);
                fireballRT.pivot = new Vector2(0.5f, 0.5f);
                fireballRT.sizeDelta = new Vector2(150, 150); // 50→150に3倍
                fireballRT.position = from;

                // 軌跡エフェクト追加
                for (int i = 0; i < 3; i++)
                {
                    var trailGO = new GameObject("Trail");
                    trailGO.transform.SetParent(canvas.transform, false);
                    var trailImg = trailGO.AddComponent<Image>();
                    trailImg.color = new Color(1f, 0.5f, 0f, 0.5f);
                    var trailRT = trailImg.rectTransform;
                    trailRT.anchorMin = new Vector2(0.5f, 0.5f);
                    trailRT.anchorMax = new Vector2(0.5f, 0.5f);
                    trailRT.pivot = new Vector2(0.5f, 0.5f);
                    trailRT.sizeDelta = new Vector2(100, 100);
                    trailRT.position = from;
                    StartCoroutine(MoveProjectile(trailRT, from, to, 0.4f + i * 0.05f, true));
                }

                StartCoroutine(MoveProjectile(fireballRT, from, to, 0.25f, true));

                // 連射の画面揺れ
                float shake = 3f;
                canvas.transform.position = originalCanvasPos + new Vector3(UnityEngine.Random.Range(-shake, shake), UnityEngine.Random.Range(-shake, shake), 0);

                // フラッシュ
                float alpha = 0.2f;
                flashImg.color = new Color(1f, 0.4f, 0f, alpha);

                yield return new UnityEngine.WaitForSeconds(shotDelay);
            }

            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
        }

        System.Collections.IEnumerator Effect_BlazeGolem(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ（爆発）
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ
            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 30f;

            // 巨大爆炎：カードの前に巨大な炎の壁（超強化版）
            var blazeGO = new GameObject("BlazeWall");
            blazeGO.transform.SetParent(canvas.transform, false);
            var blazeImg = blazeGO.AddComponent<Image>();
            blazeImg.color = new Color(1f, 0.2f, 0f, 1f);

            var blazeRT = blazeImg.rectTransform;
            blazeRT.anchorMin = new Vector2(0.5f, 0.5f);
            blazeRT.anchorMax = new Vector2(0.5f, 0.5f);
            blazeRT.pivot = new Vector2(0.5f, 0.5f);
            blazeRT.position = from + (to - from) * 0.5f;

            // 火の粉パーティクル
            var embers = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 80; i++)
            {
                var emberGO = new GameObject("Ember");
                emberGO.transform.SetParent(canvas.transform, false);
                var emberImg = emberGO.AddComponent<Image>();
                emberImg.color = new Color(1f, UnityEngine.Random.Range(0.3f, 0.7f), 0f, 1f);
                var emberRT = emberImg.rectTransform;
                emberRT.anchorMin = new Vector2(0.5f, 0.5f);
                emberRT.anchorMax = new Vector2(0.5f, 0.5f);
                emberRT.pivot = new Vector2(0.5f, 0.5f);
                emberRT.sizeDelta = new Vector2(60, 60);
                emberRT.position = blazeRT.position;

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(300f, 800f);
                Vector2 velocity = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                embers.Add((emberRT, velocity));
            }

            float duration = 1.2f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                // 超巨大化
                float size = Mathf.Lerp(200f, 2400f, t); // 800→2400に3倍
                blazeRT.sizeDelta = new Vector2(size, size * 1.5f);

                // 激しいパルス
                float pulse = 1f + Mathf.Sin(t * Mathf.PI * 15f) * 0.5f;
                blazeRT.localScale = Vector3.one * pulse;

                // フェード
                Color c = blazeImg.color;
                c.a = Mathf.Sin(t * Mathf.PI) * 1f;
                blazeImg.color = c;

                // 画面揺れ（爆発の衝撃）
                float shake = Mathf.Sin(t * 50f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);

                // フラッシュ（爆発色）
                float alpha = t < 0.3f ? Mathf.Lerp(0f, 0.8f, t / 0.3f) : Mathf.Lerp(0.8f, 0f, (t - 0.3f) / 0.7f);
                flashImg.color = new Color(1f, 0.3f, 0f, alpha);

                // 火の粉アニメーション
                foreach (var ember in embers)
                {
                    ember.rt.position += new Vector3(ember.velocity.x * dt, ember.velocity.y * dt, 0);
                    ember.rt.Rotate(0, 0, 500f * dt);
                    var emberImg = ember.rt.GetComponent<Image>();
                    Color ec = emberImg.color;
                    ec.a = Mathf.Lerp(1f, 0f, t);
                    emberImg.color = ec;
                }

                yield return null;
            }

            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            Destroy(blazeGO);
            foreach (var ember in embers) Destroy(ember.rt.gameObject);
        }

        System.Collections.IEnumerator Effect_LavaDrake(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ
            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 25f;

            // 溶岩ブレス：超太いビーム（3倍強化版）
            var beamGO = new GameObject("LavaBeam");
            beamGO.transform.SetParent(canvas.transform, false);
            var beamImg = beamGO.AddComponent<Image>();
            beamImg.color = new Color(1f, 0.4f, 0f, 0.9f);

            var beamRT = beamImg.rectTransform;
            beamRT.anchorMin = new Vector2(0.5f, 0.5f);
            beamRT.anchorMax = new Vector2(0.5f, 0.5f);
            beamRT.pivot = new Vector2(0.5f, 0.5f);
            beamRT.position = (from + to) / 2f;

            float distance = Vector3.Distance(from, to);
            float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            beamRT.rotation = Quaternion.Euler(0, 0, angle);

            // 外側の輝きビーム
            var glowBeamGO = new GameObject("GlowBeam");
            glowBeamGO.transform.SetParent(canvas.transform, false);
            var glowBeamImg = glowBeamGO.AddComponent<Image>();
            glowBeamImg.color = new Color(1f, 0.7f, 0.2f, 0.5f);
            var glowBeamRT = glowBeamImg.rectTransform;
            glowBeamRT.anchorMin = new Vector2(0.5f, 0.5f);
            glowBeamRT.anchorMax = new Vector2(0.5f, 0.5f);
            glowBeamRT.pivot = new Vector2(0.5f, 0.5f);
            glowBeamRT.position = (from + to) / 2f;
            glowBeamRT.rotation = Quaternion.Euler(0, 0, angle);

            // 溶岩パーティクル
            var lavaParticles = new List<(RectTransform rt, float offset)>();
            for (int i = 0; i < 60; i++)
            {
                var particleGO = new GameObject("LavaParticle");
                particleGO.transform.SetParent(canvas.transform, false);
                var particleImg = particleGO.AddComponent<Image>();
                particleImg.color = new Color(1f, UnityEngine.Random.Range(0.2f, 0.6f), 0f, 1f);
                var particleRT = particleImg.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(80, 80);
                lavaParticles.Add((particleRT, UnityEngine.Random.Range(0f, 1f)));
            }

            float duration = 1.0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                // 超太いビーム（3倍）
                float width = Mathf.Lerp(30f, 240f, Mathf.Sin(t * Mathf.PI)); // 80→240に3倍
                beamRT.sizeDelta = new Vector2(distance, width);
                glowBeamRT.sizeDelta = new Vector2(distance, width * 1.5f);

                // パルス
                float pulse = 1f + Mathf.Sin(t * Mathf.PI * 20f) * 0.3f;
                beamRT.localScale = Vector3.one * pulse;

                // フェード
                Color c = beamImg.color;
                c.a = Mathf.Sin(t * Mathf.PI) * 0.9f;
                beamImg.color = c;

                Color gc = glowBeamImg.color;
                gc.a = Mathf.Sin(t * Mathf.PI) * 0.5f;
                glowBeamImg.color = gc;

                // 画面揺れ
                float shake = Mathf.Sin(t * 40f) * shakeIntensity * Mathf.Sin(t * Mathf.PI);
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.3f, 0);

                // フラッシュ（溶岩色）
                float alpha = Mathf.Sin(t * Mathf.PI) * 0.6f;
                flashImg.color = new Color(1f, 0.4f, 0f, alpha);

                // 溶岩パーティクルをビームに沿って流す
                foreach (var particle in lavaParticles)
                {
                    float progress = (t + particle.offset) % 1f;
                    Vector3 particlePos = Vector3.Lerp(from, to, progress);
                    particlePos += new Vector3(UnityEngine.Random.Range(-30f, 30f), UnityEngine.Random.Range(-30f, 30f), 0);
                    particle.rt.position = particlePos;
                    particle.rt.Rotate(0, 0, 800f * dt);

                    var pImg = particle.rt.GetComponent<Image>();
                    Color pc = pImg.color;
                    pc.a = Mathf.Sin(progress * Mathf.PI);
                    pImg.color = pc;
                }

                yield return null;
            }

            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            Destroy(beamGO);
            Destroy(glowBeamGO);
            foreach (var particle in lavaParticles) Destroy(particle.rt.gameObject);
        }

        System.Collections.IEnumerator Effect_CinderFox(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 炎の尾旋回：カード周りを炎が円を描く（超強化版）
            int tailCount = 36; // 12→36に3倍
            var tails = new List<RectTransform>();

            for (int i = 0; i < tailCount; i++)
            {
                var tailGO = new GameObject($"Tail{i}");
                tailGO.transform.SetParent(canvas.transform, false);
                var tailImg = tailGO.AddComponent<Image>();
                tailImg.color = new Color(1f, UnityEngine.Random.Range(0.3f, 0.7f), 0f, 0.9f);

                var tailRT = tailImg.rectTransform;
                tailRT.anchorMin = new Vector2(0.5f, 0.5f);
                tailRT.anchorMax = new Vector2(0.5f, 0.5f);
                tailRT.pivot = new Vector2(0.5f, 0.5f);
                tailRT.sizeDelta = new Vector2(75, 75); // 25→75に3倍

                tails.Add(tailRT);
            }

            // 追加の火花パーティクル
            var sparkles = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 50; i++)
            {
                var sparkleGO = new GameObject("Sparkle");
                sparkleGO.transform.SetParent(canvas.transform, false);
                var sparkleImg = sparkleGO.AddComponent<Image>();
                sparkleImg.color = new Color(1f, 0.7f, 0.2f, 1f);
                var sparkleRT = sparkleImg.rectTransform;
                sparkleRT.anchorMin = new Vector2(0.5f, 0.5f);
                sparkleRT.anchorMax = new Vector2(0.5f, 0.5f);
                sparkleRT.pivot = new Vector2(0.5f, 0.5f);
                sparkleRT.sizeDelta = new Vector2(30, 30);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(200f, 500f);
                Vector2 velocity = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                sparkles.Add((sparkleRT, velocity));
            }

            float duration = 1.0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                // 炎の尾の高速回転
                for (int i = 0; i < tails.Count; i++)
                {
                    float angle = (360f / tailCount) * i + elapsed * 1440f; // 4回転/秒（2倍速）
                    float radius = 150f + Mathf.Sin(t * Mathf.PI * 3f) * 100f; // 100→150、振幅2倍

                    float x = Mathf.Cos(angle * Mathf.Deg2Rad) * radius;
                    float y = Mathf.Sin(angle * Mathf.Deg2Rad) * radius;

                    tails[i].position = from + new Vector3(x, y, 0);
                    tails[i].Rotate(0, 0, 1000f * dt);

                    var img = tails[i].GetComponent<Image>();
                    Color c = img.color;
                    c.a = Mathf.Sin(t * Mathf.PI) * 0.9f;
                    img.color = c;

                    // サイズパルス
                    float scale = 1f + Mathf.Sin((t + i * 0.1f) * Mathf.PI * 5f) * 0.5f;
                    tails[i].localScale = Vector3.one * scale;
                }

                // フラッシュ
                float alpha = Mathf.Sin(t * Mathf.PI * 3f) * 0.3f;
                flashImg.color = new Color(1f, 0.5f, 0f, alpha);

                // 火花パーティクル
                foreach (var sparkle in sparkles)
                {
                    sparkle.rt.position = from + new Vector3(sparkle.velocity.x * t, sparkle.velocity.y * t, 0);
                    sparkle.rt.Rotate(0, 0, 800f * dt);
                    var sImg = sparkle.rt.GetComponent<Image>();
                    Color sc = sImg.color;
                    sc.a = Mathf.Lerp(1f, 0f, t);
                    sImg.color = sc;
                }

                yield return null;
            }

            Destroy(flash);
            foreach (var tail in tails) Destroy(tail.gameObject);
            foreach (var sparkle in sparkles) Destroy(sparkle.rt.gameObject);
        }

        // ========== Water属性演出 ==========

        System.Collections.IEnumerator Effect_WaterSpirit(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ（水色）
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 水の渦巻き（超強化版）
            int dropCount = 90; // 30→90に3倍
            var drops = new List<(RectTransform rt, float angle, float radius)>();

            for (int i = 0; i < dropCount; i++)
            {
                var dropGO = new GameObject($"Drop{i}");
                dropGO.transform.SetParent(canvas.transform, false);
                var dropImg = dropGO.AddComponent<Image>();
                dropImg.color = new Color(0.2f, 0.6f, 1f, 0.9f);

                var dropRT = dropImg.rectTransform;
                dropRT.anchorMin = new Vector2(0.5f, 0.5f);
                dropRT.anchorMax = new Vector2(0.5f, 0.5f);
                dropRT.pivot = new Vector2(0.5f, 0.5f);
                dropRT.sizeDelta = new Vector2(45, 45); // 15→45に3倍

                float angle = (360f / dropCount) * i;
                float radius = 250f; // より大きく

                drops.Add((dropRT, angle, radius));
            }

            // 水しぶきパーティクル
            var splashes = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 50; i++)
            {
                var splashGO = new GameObject("Splash");
                splashGO.transform.SetParent(canvas.transform, false);
                var splashImg = splashGO.AddComponent<Image>();
                splashImg.color = new Color(0.4f, 0.8f, 1f, 1f);
                var splashRT = splashImg.rectTransform;
                splashRT.anchorMin = new Vector2(0.5f, 0.5f);
                splashRT.anchorMax = new Vector2(0.5f, 0.5f);
                splashRT.pivot = new Vector2(0.5f, 0.5f);
                splashRT.sizeDelta = new Vector2(40, 40);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(200f, 600f);
                Vector2 velocity = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                splashes.Add((splashRT, velocity));
            }

            float duration = 1.2f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                for (int i = 0; i < drops.Count; i++)
                {
                    var drop = drops[i];
                    float currentAngle = drop.angle + elapsed * 720f; // 2回転/秒（2倍速）
                    float currentRadius = drop.radius * (1f - t * 0.7f); // 徐々に縮小

                    float x = Mathf.Cos(currentAngle * Mathf.Deg2Rad) * currentRadius;
                    float y = Mathf.Sin(currentAngle * Mathf.Deg2Rad) * currentRadius;

                    drop.rt.position = from + new Vector3(x, y, 0);

                    var img = drop.rt.GetComponent<Image>();
                    Color c = img.color;
                    c.a = Mathf.Sin(t * Mathf.PI) * 0.9f;
                    img.color = c;

                    // サイズパルス
                    float scale = 1f + Mathf.Sin((t + i * 0.05f) * Mathf.PI * 4f) * 0.4f;
                    drop.rt.localScale = Vector3.one * scale;
                }

                // フラッシュ
                float alpha = Mathf.Sin(t * Mathf.PI * 2f) * 0.4f;
                flashImg.color = new Color(0.2f, 0.6f, 1f, alpha);

                // 水しぶき
                foreach (var splash in splashes)
                {
                    splash.rt.position = from + new Vector3(splash.velocity.x * t, splash.velocity.y * t, 0);
                    var sImg = splash.rt.GetComponent<Image>();
                    Color sc = sImg.color;
                    sc.a = Mathf.Lerp(1f, 0f, t);
                    sImg.color = sc;
                }

                yield return null;
            }

            Destroy(flash);
            foreach (var drop in drops) Destroy(drop.rt.gameObject);
            foreach (var splash in splashes) Destroy(splash.rt.gameObject);
        }

        System.Collections.IEnumerator Effect_TideCaller(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ
            Vector3 originalCanvasPos = canvas.transform.position;

            // 波動砲：9連波（3倍）
            for (int w = 0; w < 9; w++)
            {
                var waveGO = new GameObject($"Wave{w}");
                waveGO.transform.SetParent(canvas.transform, false);
                var waveImg = waveGO.AddComponent<Image>();
                waveImg.color = new Color(0.3f, 0.7f, 1f, 0.9f);

                var waveRT = waveImg.rectTransform;
                waveRT.anchorMin = new Vector2(0.5f, 0.5f);
                waveRT.anchorMax = new Vector2(0.5f, 0.5f);
                waveRT.pivot = new Vector2(0.5f, 0.5f);
                waveRT.sizeDelta = new Vector2(180, 180); // 60→180に3倍
                waveRT.position = from;

                // 波紋エフェクトも追加
                for (int i = 0; i < 3; i++)
                {
                    var rippleGO = new GameObject("Ripple");
                    rippleGO.transform.SetParent(canvas.transform, false);
                    var rippleImg = rippleGO.AddComponent<Image>();
                    rippleImg.color = new Color(0.5f, 0.8f, 1f, 0.5f);
                    var rippleRT = rippleImg.rectTransform;
                    rippleRT.anchorMin = new Vector2(0.5f, 0.5f);
                    rippleRT.anchorMax = new Vector2(0.5f, 0.5f);
                    rippleRT.pivot = new Vector2(0.5f, 0.5f);
                    rippleRT.sizeDelta = new Vector2(120, 120);
                    rippleRT.position = from;
                    StartCoroutine(MoveProjectile(rippleRT, from, to, 0.3f + i * 0.1f, true));
                }

                StartCoroutine(MoveProjectile(waveRT, from, to, 0.25f, true));

                // 連射の画面揺れ
                float shake = 5f;
                canvas.transform.position = originalCanvasPos + new Vector3(UnityEngine.Random.Range(-shake, shake), UnityEngine.Random.Range(-shake, shake), 0);

                // フラッシュ
                flashImg.color = new Color(0.3f, 0.7f, 1f, 0.3f);

                yield return new UnityEngine.WaitForSeconds(0.08f); // より速く
            }

            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
        }

        System.Collections.IEnumerator Effect_IceGuardian(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ（氷色）
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ
            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 20f;

            // 氷の盾＋氷柱：カード前に巨大な氷の壁、そこから氷柱発射（超強化版）
            var shieldGO = new GameObject("IceShield");
            shieldGO.transform.SetParent(canvas.transform, false);
            var shieldImg = shieldGO.AddComponent<Image>();
            shieldImg.color = new Color(0.6f, 0.8f, 1f, 0.9f);

            var shieldRT = shieldImg.rectTransform;
            shieldRT.anchorMin = new Vector2(0.5f, 0.5f);
            shieldRT.anchorMax = new Vector2(0.5f, 0.5f);
            shieldRT.pivot = new Vector2(0.5f, 0.5f);
            shieldRT.position = from + (to - from) * 0.3f;
            shieldRT.sizeDelta = new Vector2(300, 600); // 100x200→300x600に3倍

            // 盾のパルスアニメーション
            StartCoroutine(PulseScale(shieldRT, 1.0f));

            // 氷柱発射（18本に3倍）
            for (int i = 0; i < 18; i++)
            {
                var icicleGO = new GameObject($"Icicle{i}");
                icicleGO.transform.SetParent(canvas.transform, false);
                var icicleImg = icicleGO.AddComponent<Image>();
                icicleImg.color = new Color(0.7f, 0.9f, 1f, 1f);

                var icicleRT = icicleImg.rectTransform;
                icicleRT.anchorMin = new Vector2(0.5f, 0.5f);
                icicleRT.anchorMax = new Vector2(0.5f, 0.5f);
                icicleRT.pivot = new Vector2(0.5f, 0.5f);
                icicleRT.sizeDelta = new Vector2(45, 120); // 15x40→45x120に3倍

                Vector3 spread = new Vector3(0, UnityEngine.Random.Range(-150f, 150f), 0);
                StartCoroutine(MoveProjectile(icicleRT, shieldRT.position, to + spread, 0.4f, true));

                // 画面揺れ
                float shake = Mathf.Sin(i * 0.5f) * shakeIntensity * 0.5f;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.3f, 0);

                // フラッシュ
                flashImg.color = new Color(0.6f, 0.8f, 1f, 0.2f);

                yield return new UnityEngine.WaitForSeconds(0.05f);
            }

            canvas.transform.position = originalCanvasPos;
            yield return new UnityEngine.WaitForSeconds(0.3f);
            Destroy(flash);
            Destroy(shieldGO);
        }

        IEnumerator PulseScale(RectTransform rt, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float scale = 1f + Mathf.Sin(elapsed * Mathf.PI * 5f) * 0.2f;
                rt.localScale = Vector3.one * scale;
                yield return null;
            }
        }

        System.Collections.IEnumerator Effect_StreamPixie(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 水玉シャワー：上から大量に降る（超強化版）
            int dropCount = 45; // 15→45に3倍

            for (int i = 0; i < dropCount; i++)
            {
                var dropGO = new GameObject($"Shower{i}");
                dropGO.transform.SetParent(canvas.transform, false);
                var dropImg = dropGO.AddComponent<Image>();
                dropImg.color = new Color(0.4f, 0.7f, 1f, 1f);

                var dropRT = dropImg.rectTransform;
                dropRT.anchorMin = new Vector2(0.5f, 0.5f);
                dropRT.anchorMax = new Vector2(0.5f, 0.5f);
                dropRT.pivot = new Vector2(0.5f, 0.5f);
                dropRT.sizeDelta = new Vector2(36, 36); // 12→36に3倍

                Vector3 startPos = to + new Vector3(UnityEngine.Random.Range(-300f, 300f), 600f, 0);
                Vector3 endPos = to + new Vector3(UnityEngine.Random.Range(-150f, 150f), -200f, 0);

                StartCoroutine(MoveProjectile(dropRT, startPos, endPos, UnityEngine.Random.Range(0.3f, 0.6f), true));

                // フラッシュ
                if (i % 3 == 0)
                {
                    flashImg.color = new Color(0.4f, 0.7f, 1f, 0.1f);
                }
            }

            yield return new UnityEngine.WaitForSeconds(0.8f);
            Destroy(flash);
        }

        System.Collections.IEnumerator Effect_AquaSerpent(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 水蛇の噛みつき：蛇行する巨大な水の軌跡（超強化版）
            var serpentGO = new GameObject("Serpent");
            serpentGO.transform.SetParent(canvas.transform, false);
            var serpentImg = serpentGO.AddComponent<Image>();
            serpentImg.color = new Color(0.2f, 0.5f, 0.9f, 1f);

            var serpentRT = serpentImg.rectTransform;
            serpentRT.anchorMin = new Vector2(0.5f, 0.5f);
            serpentRT.anchorMax = new Vector2(0.5f, 0.5f);
            serpentRT.pivot = new Vector2(0.5f, 0.5f);
            serpentRT.sizeDelta = new Vector2(120, 120); // 40→120に3倍
            serpentRT.position = from;

            // 水の軌跡パーティクル
            var trails = new List<GameObject>();
            for (int i = 0; i < 30; i++)
            {
                var trailGO = new GameObject("Trail");
                trailGO.transform.SetParent(canvas.transform, false);
                var trailImg = trailGO.AddComponent<Image>();
                trailImg.color = new Color(0.4f, 0.7f, 1f, 0.7f);
                var trailRT = trailImg.rectTransform;
                trailRT.anchorMin = new Vector2(0.5f, 0.5f);
                trailRT.anchorMax = new Vector2(0.5f, 0.5f);
                trailRT.pivot = new Vector2(0.5f, 0.5f);
                trailRT.sizeDelta = new Vector2(80, 80);
                trails.Add(trailGO);
            }

            float duration = 0.8f;
            float elapsed = 0f;
            int trailIndex = 0;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 蛇行移動（より大きく）
                Vector3 basePos = Vector3.Lerp(from, to, t);
                float wave = Mathf.Sin(t * Mathf.PI * 6f) * 150f; // 50→150に3倍
                Vector3 perpendicular = new Vector3(-(to.y - from.y), to.x - from.x, 0).normalized;
                serpentRT.position = basePos + perpendicular * wave;

                // 回転
                serpentRT.Rotate(0, 0, 1500f * Time.deltaTime);

                // サイズパルス
                float scale = 1f + Mathf.Sin(t * Mathf.PI * 10f) * 0.3f;
                serpentRT.localScale = Vector3.one * scale;

                // 軌跡を残す
                if (trailIndex < trails.Count)
                {
                    trails[trailIndex].GetComponent<RectTransform>().position = serpentRT.position;
                    StartCoroutine(FadeOutAndDestroy(trails[trailIndex], 0.5f));
                    trailIndex++;
                }

                // フラッシュ
                float alpha = Mathf.Sin(t * Mathf.PI * 3f) * 0.3f;
                flashImg.color = new Color(0.2f, 0.5f, 0.9f, alpha);

                yield return null;
            }

            Destroy(flash);
            Destroy(serpentGO);
        }

        IEnumerator FadeOutAndDestroy(GameObject obj, float duration)
        {
            var img = obj.GetComponent<Image>();
            float elapsed = 0f;
            Color startColor = img.color;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(startColor.a, 0f, elapsed / duration);
                img.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
                yield return null;
            }
            Destroy(obj);
        }

        // ========== Nature属性演出 ==========

        System.Collections.IEnumerator Effect_ForestElf(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ（緑）
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 葉っぱの大嵐（超強化版）
            int leafCount = 100; // 20→100に5倍
            var leaves = new List<(RectTransform rt, Vector2 velocity)>();

            for (int i = 0; i < leafCount; i++)
            {
                var leafGO = new GameObject($"Leaf{i}");
                leafGO.transform.SetParent(canvas.transform, false);
                var leafImg = leafGO.AddComponent<Image>();
                leafImg.color = new Color(UnityEngine.Random.Range(0.2f, 0.5f), UnityEngine.Random.Range(0.7f, 1f), UnityEngine.Random.Range(0.2f, 0.4f), 1f);

                var leafRT = leafImg.rectTransform;
                leafRT.anchorMin = new Vector2(0.5f, 0.5f);
                leafRT.anchorMax = new Vector2(0.5f, 0.5f);
                leafRT.pivot = new Vector2(0.5f, 0.5f);
                leafRT.sizeDelta = new Vector2(UnityEngine.Random.Range(60f, 120f), UnityEngine.Random.Range(40f, 80f)); // 葉っぱ型

                // 画面全体から発生
                leafRT.position = from + new Vector3(UnityEngine.Random.Range(-400f, 400f), UnityEngine.Random.Range(-300f, 300f), 0);

                Vector2 velocity = new Vector2(
                    UnityEngine.Random.Range(-400f, 400f),
                    UnityEngine.Random.Range(-200f, 400f)
                );

                leaves.Add((leafRT, velocity));
            }

            // 竜巻エフェクト
            var tornadoParticles = new List<(RectTransform rt, float angle, float radius)>();
            for (int i = 0; i < 50; i++)
            {
                var particleGO = new GameObject("TornadoParticle");
                particleGO.transform.SetParent(canvas.transform, false);
                var particleImg = particleGO.AddComponent<Image>();
                particleImg.color = new Color(0.4f, 0.9f, 0.4f, 0.7f);
                var particleRT = particleImg.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(30, 30);
                tornadoParticles.Add((particleRT, i * (360f / 50f), UnityEngine.Random.Range(100f, 300f)));
            }

            float duration = 1.5f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                foreach (var leaf in leaves)
                {
                    // 激しいひらひら移動
                    Vector3 drift = new Vector3(Mathf.Sin(elapsed * 10f) * 150f * dt, Mathf.Cos(elapsed * 8f) * 100f * dt, 0);
                    leaf.rt.position += new Vector3(leaf.velocity.x * dt, leaf.velocity.y * dt, 0) + drift;

                    // 激しい回転
                    leaf.rt.Rotate(0, 0, UnityEngine.Random.Range(-800f, 800f) * dt);

                    // フェード
                    var img = leaf.rt.GetComponent<Image>();
                    Color c = img.color;
                    c.a = Mathf.Lerp(1f, 0f, t);
                    img.color = c;
                }

                // 竜巻エフェクト
                Vector3 tornadoCenter = (from + to) / 2f;
                foreach (var particle in tornadoParticles)
                {
                    float currentAngle = particle.angle + elapsed * 720f; // 高速回転
                    float currentRadius = particle.radius * (1f + Mathf.Sin(t * Mathf.PI) * 0.5f);
                    float height = Mathf.Sin(t * Mathf.PI) * 400f;

                    float x = Mathf.Cos(currentAngle * Mathf.Deg2Rad) * currentRadius;
                    float y = Mathf.Sin(currentAngle * Mathf.Deg2Rad) * currentRadius + height;

                    particle.rt.position = tornadoCenter + new Vector3(x, y, 0);
                }

                // フラッシュ
                float alpha = Mathf.Sin(t * Mathf.PI * 4f) * 0.4f;
                flashImg.color = new Color(0.3f, 0.9f, 0.3f, alpha);

                yield return null;
            }

            Destroy(flash);
            foreach (var leaf in leaves) Destroy(leaf.rt.gameObject);
            foreach (var particle in tornadoParticles) Destroy(particle.rt.gameObject);
        }

        System.Collections.IEnumerator Effect_VineBeast(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ
            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 25f;

            // 巨大ツタの鞭：複数の太いツタが画面を覆う（超強化版）
            var vines = new List<(RectTransform rt, float targetLength, float angle)>();

            // 複数のツタを生成
            for (int v = 0; v < 8; v++)
            {
                var vineGO = new GameObject($"Vine{v}");
                vineGO.transform.SetParent(canvas.transform, false);
                var vineImg = vineGO.AddComponent<Image>();
                vineImg.color = new Color(UnityEngine.Random.Range(0.2f, 0.4f), UnityEngine.Random.Range(0.5f, 0.7f), 0.2f, 0.95f);

                var vineRT = vineImg.rectTransform;
                vineRT.anchorMin = new Vector2(0.5f, 0.5f);
                vineRT.anchorMax = new Vector2(0.5f, 0.5f);
                vineRT.pivot = new Vector2(0, 0.5f);
                vineRT.position = from;

                float distance = Vector3.Distance(from, to);
                float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg + UnityEngine.Random.Range(-30f, 30f);
                vineRT.rotation = Quaternion.Euler(0, 0, angle);

                vines.Add((vineRT, distance * UnityEngine.Random.Range(1.2f, 2.0f), angle));
            }

            // トゲパーティクル
            var thorns = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 60; i++)
            {
                var thornGO = new GameObject("Thorn");
                thornGO.transform.SetParent(canvas.transform, false);
                var thornImg = thornGO.AddComponent<Image>();
                thornImg.color = new Color(0.5f, 0.3f, 0.1f, 1f);
                var thornRT = thornImg.rectTransform;
                thornRT.anchorMin = new Vector2(0.5f, 0.5f);
                thornRT.anchorMax = new Vector2(0.5f, 0.5f);
                thornRT.pivot = new Vector2(0.5f, 0.5f);
                thornRT.sizeDelta = new Vector2(30, 60);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(300f, 700f);
                Vector2 velocity = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                thorns.Add((thornRT, velocity));
            }

            float duration = 1.0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                foreach (var vine in vines)
                {
                    // 激しく伸びる
                    float length = Mathf.Lerp(0f, vine.targetLength, Mathf.Pow(t, 0.7f));
                    vine.rt.sizeDelta = new Vector2(length, UnityEngine.Random.Range(60f, 100f)); // 超太い

                    // 激しくうねうね
                    float wave = Mathf.Sin(t * Mathf.PI * 15f + vine.angle) * 20f;
                    vine.rt.anchoredPosition = new Vector2(0, wave);

                    // 回転も
                    float rotationOffset = Mathf.Sin(t * Mathf.PI * 5f) * 10f;
                    vine.rt.rotation = Quaternion.Euler(0, 0, vine.angle + rotationOffset);
                }

                // 画面揺れ
                float shake = Mathf.Sin(t * 50f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);

                // フラッシュ
                float alpha = t < 0.3f ? Mathf.Lerp(0f, 0.6f, t / 0.3f) : Mathf.Lerp(0.6f, 0f, (t - 0.3f) / 0.7f);
                flashImg.color = new Color(0.3f, 0.6f, 0.2f, alpha);

                // トゲ
                foreach (var thorn in thorns)
                {
                    thorn.rt.position = from + new Vector3(thorn.velocity.x * t, thorn.velocity.y * t, 0);
                    thorn.rt.Rotate(0, 0, 1000f * dt);
                    var thornImg = thorn.rt.GetComponent<Image>();
                    Color tc = thornImg.color;
                    tc.a = Mathf.Lerp(1f, 0f, t);
                    thornImg.color = tc;
                }

                yield return null;
            }

            canvas.transform.position = originalCanvasPos;
            yield return new UnityEngine.WaitForSeconds(0.3f);
            Destroy(flash);
            foreach (var vine in vines) Destroy(vine.rt.gameObject);
            foreach (var thorn in thorns) Destroy(thorn.rt.gameObject);
        }

        System.Collections.IEnumerator Effect_GroveGuardian(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ（緑）
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ（地震）
            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 40f; // 最大級の揺れ

            // 巨木召喚：画面を突き破る超巨大な木（超強化版）
            var treeGO = new GameObject("Tree");
            treeGO.transform.SetParent(canvas.transform, false);
            var treeText = treeGO.AddComponent<Text>();
            treeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            treeText.fontSize = 400; // 120→400に超巨大化
            treeText.fontStyle = FontStyle.Bold;
            treeText.text = "森";
            treeText.color = new Color(0.4f, 0.7f, 0.2f, 1f);
            treeText.alignment = TextAnchor.MiddleCenter;

            var outline = treeGO.AddComponent<Outline>();
            outline.effectColor = new Color(0.2f, 0.4f, 0.1f);
            outline.effectDistance = new Vector2(10, -10);

            var treeRT = treeText.rectTransform;
            treeRT.anchorMin = new Vector2(0.5f, 0.5f);
            treeRT.anchorMax = new Vector2(0.5f, 0.5f);
            treeRT.pivot = new Vector2(0.5f, 0f); // 下から成長
            treeRT.sizeDelta = new Vector2(800, 800);
            treeRT.position = new Vector3(from.x, from.y - 400f, 0); // 画面下から
            treeRT.localScale = new Vector3(1f, 0f, 1f);

            // 葉っぱパーティクル爆発
            var leaves = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 150; i++)
            {
                var leafGO = new GameObject("Leaf");
                leafGO.transform.SetParent(canvas.transform, false);
                var leafImg = leafGO.AddComponent<Image>();
                leafImg.color = new Color(UnityEngine.Random.Range(0.3f, 0.6f), UnityEngine.Random.Range(0.7f, 1f), UnityEngine.Random.Range(0.2f, 0.4f), 1f);
                var leafRT = leafImg.rectTransform;
                leafRT.anchorMin = new Vector2(0.5f, 0.5f);
                leafRT.anchorMax = new Vector2(0.5f, 0.5f);
                leafRT.pivot = new Vector2(0.5f, 0.5f);
                leafRT.sizeDelta = new Vector2(80, 50);
                leafRT.position = from;

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(400f, 1000f);
                Vector2 velocity = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                leaves.Add((leafRT, velocity));
            }

            // 根っこエフェクト
            var roots = new List<RectTransform>();
            for (int i = 0; i < 12; i++)
            {
                var rootGO = new GameObject($"Root{i}");
                rootGO.transform.SetParent(canvas.transform, false);
                var rootImg = rootGO.AddComponent<Image>();
                rootImg.color = new Color(0.3f, 0.2f, 0.1f, 0.8f);
                var rootRT = rootImg.rectTransform;
                rootRT.anchorMin = new Vector2(0.5f, 0.5f);
                rootRT.anchorMax = new Vector2(0.5f, 0.5f);
                rootRT.pivot = new Vector2(0, 0.5f);
                rootRT.position = from;
                rootRT.rotation = Quaternion.Euler(0, 0, i * 30f);
                roots.Add(rootRT);
            }

            float duration = 1.5f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                // 巨木が下から爆発的に成長
                float scaleY = Mathf.Lerp(0f, 3.0f, Mathf.Pow(t, 0.4f)); // 超巨大化
                treeRT.localScale = new Vector3(1.2f, scaleY, 1f);

                // 根っこが広がる
                foreach (var root in roots)
                {
                    float rootLength = Mathf.Lerp(0f, 600f, Mathf.Pow(t, 0.6f));
                    root.sizeDelta = new Vector2(rootLength, 40f);
                }

                // フェード
                Color c = treeText.color;
                c.a = Mathf.Sin(t * Mathf.PI);
                treeText.color = c;

                // 激しい画面揺れ（地震）
                float shake = Mathf.Sin(t * 60f) * shakeIntensity * Mathf.Sin(t * Mathf.PI);
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.7f, 0);

                // フラッシュ（緑の爆発）
                float alpha = t < 0.4f ? Mathf.Lerp(0f, 0.9f, t / 0.4f) : Mathf.Lerp(0.9f, 0f, (t - 0.4f) / 0.6f);
                flashImg.color = new Color(0.4f, 0.8f, 0.3f, alpha);

                // 葉っぱ大爆発
                foreach (var leaf in leaves)
                {
                    leaf.rt.position += new Vector3(leaf.velocity.x * dt, leaf.velocity.y * dt, 0);
                    leaf.rt.Rotate(0, 0, 1200f * dt);
                    var leafImg = leaf.rt.GetComponent<Image>();
                    Color lc = leafImg.color;
                    lc.a = Mathf.Lerp(1f, 0f, t);
                    leafImg.color = lc;
                }

                yield return null;
            }

            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            Destroy(treeGO);
            foreach (var root in roots) Destroy(root.gameObject);
            foreach (var leaf in leaves) Destroy(leaf.rt.gameObject);
        }

        System.Collections.IEnumerator Effect_MossTurtle(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 画面フラッシュ（緑）
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 画面揺れ
            Vector3 originalCanvasPos = canvas.transform.position;

            // 苔の超巨大シールド：画面全体を覆う緑の防壁（超強化版）
            var shields = new List<GameObject>();

            // 複数層の盾
            for (int layer = 0; layer < 5; layer++)
            {
                var mossGO = new GameObject($"MossShield{layer}");
                mossGO.transform.SetParent(canvas.transform, false);
                var mossImg = mossGO.AddComponent<Image>();
                mossImg.color = new Color(0.3f + layer * 0.1f, 0.6f + layer * 0.05f, 0.2f + layer * 0.05f, 0.7f - layer * 0.1f);

                var mossRT = mossImg.rectTransform;
                mossRT.anchorMin = new Vector2(0.5f, 0.5f);
                mossRT.anchorMax = new Vector2(0.5f, 0.5f);
                mossRT.pivot = new Vector2(0.5f, 0.5f);
                mossRT.position = from + new Vector3(layer * 50f, 0, 0);

                shields.Add(mossGO);
            }

            // 苔パーティクル爆発
            var mossParticles = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 100; i++)
            {
                var particleGO = new GameObject("MossParticle");
                particleGO.transform.SetParent(canvas.transform, false);
                var particleImg = particleGO.AddComponent<Image>();
                particleImg.color = new Color(UnityEngine.Random.Range(0.3f, 0.5f), UnityEngine.Random.Range(0.6f, 0.8f), UnityEngine.Random.Range(0.2f, 0.4f), 1f);
                var particleRT = particleImg.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(40, 40);
                particleRT.position = from;

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(200f, 600f);
                Vector2 velocity = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                mossParticles.Add((particleRT, velocity));
            }

            float duration = 1.2f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float dt = Time.deltaTime;

                // 盾が爆発的に展開
                for (int i = 0; i < shields.Count; i++)
                {
                    var shield = shields[i].GetComponent<RectTransform>();
                    float delay = i * 0.1f;
                    float layerT = Mathf.Clamp01((t - delay) / (1f - delay));
                    float size = Mathf.Lerp(100f, 1200f, Mathf.Pow(layerT, 0.5f)); // 超巨大化
                    shield.sizeDelta = new Vector2(size * 0.8f, size);

                    // パルス
                    float pulse = 1f + Mathf.Sin((t + i * 0.2f) * Mathf.PI * 8f) * 0.2f;
                    shield.localScale = Vector3.one * pulse;

                    // フェード
                    var img = shields[i].GetComponent<Image>();
                    Color c = img.color;
                    c.a = Mathf.Sin(layerT * Mathf.PI) * (0.7f - i * 0.1f);
                    img.color = c;
                }

                // 画面揺れ
                float shake = Mathf.Sin(t * 30f) * 10f * Mathf.Sin(t * Mathf.PI);
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);

                // フラッシュ
                float alpha = Mathf.Sin(t * Mathf.PI * 3f) * 0.5f;
                flashImg.color = new Color(0.4f, 0.7f, 0.3f, alpha);

                // 苔パーティクル
                foreach (var particle in mossParticles)
                {
                    particle.rt.position = from + new Vector3(particle.velocity.x * t, particle.velocity.y * t, 0);
                    particle.rt.Rotate(0, 0, 500f * dt);
                    var pImg = particle.rt.GetComponent<Image>();
                    Color pc = pImg.color;
                    pc.a = Mathf.Lerp(1f, 0f, t);
                    pImg.color = pc;
                }

                yield return null;
            }

            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var shield in shields) Destroy(shield);
            foreach (var particle in mossParticles) Destroy(particle.rt.gameObject);
        }

        System.Collections.IEnumerator Effect_ThornStalker(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大トゲ嵐：画面を覆う猛烈なトゲの雨
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 20f;
            float duration = 1.5f;
            float elapsed = 0f;

            // 100個の巨大トゲ弾幕
            var thornList = new List<(RectTransform rt, Vector2 velocity, float spin)>();
            for (int i = 0; i < 100; i++)
            {
                var thornGO = new GameObject($"Thorn{i}");
                thornGO.transform.SetParent(canvas.transform, false);
                var thornImg = thornGO.AddComponent<Image>();
                thornImg.color = new Color(0.4f, 0.6f, 0.2f, 0.9f);

                var thornRT = thornImg.rectTransform;
                thornRT.anchorMin = new Vector2(0.5f, 0.5f);
                thornRT.anchorMax = new Vector2(0.5f, 0.5f);
                thornRT.pivot = new Vector2(0.5f, 0.5f);

                // サイズ: 60x120 (3倍超巨大化)
                float size = UnityEngine.Random.Range(60f, 120f);
                thornRT.sizeDelta = new Vector2(size * 0.4f, size);
                thornRT.position = from;

                // 放射状に発射
                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(800f, 1400f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                float spin = UnityEngine.Random.Range(-720f, 720f);

                thornList.Add((thornRT, vel, spin));
            }

            // 120個の破片パーティクル（茨エフェクト）
            var particleList = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 120; i++)
            {
                var particleGO = new GameObject($"ThornParticle{i}");
                particleGO.transform.SetParent(canvas.transform, false);
                var particleText = particleGO.AddComponent<Text>();
                particleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                particleText.fontSize = UnityEngine.Random.Range(30, 60);
                particleText.text = "棘";
                particleText.color = new Color(0.3f, 0.5f, 0.1f, 1f);
                particleText.alignment = TextAnchor.MiddleCenter;

                var particleRT = particleText.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(50, 50);
                particleRT.position = to;

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(300f, 600f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                particleList.Add((particleRT, vel));
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 画面揺れ（トゲ嵐の振動）
                float shake = Mathf.Sin(t * 50f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.7f, 0);

                // 緑褐色フラッシュ
                float alpha = t < 0.15f ? Mathf.Lerp(0f, 0.5f, t / 0.15f) : Mathf.Lerp(0.5f, 0f, (t - 0.15f) / 0.85f);
                flashImg.color = new Color(0.4f, 0.6f, 0.2f, alpha);

                // トゲの移動と回転
                foreach (var (rt, velocity, spin) in thornList)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);
                        rt.Rotate(0, 0, spin * Time.deltaTime);

                        // フェードアウト
                        var img = rt.GetComponent<Image>();
                        if (img != null)
                        {
                            Color c = img.color;
                            c.a = 0.9f * (1f - t);
                            img.color = c;
                        }
                    }
                }

                // パーティクル移動
                foreach (var (rt, velocity) in particleList)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);

                        // フェードアウト
                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = 1f - t;
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var (rt, _, _) in thornList)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var (rt, _) in particleList)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        // ========== Earth属性演出 ==========

        System.Collections.IEnumerator Effect_RockGiant(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大地割れパンチ：画面全体を裂く大地震
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 35f; // 超強力な揺れ
            float duration = 1.8f;
            float elapsed = 0f;

            // 24本の巨大亀裂（画面全体を覆う）
            var cracks = new List<RectTransform>();
            for (int i = 0; i < 24; i++)
            {
                var crackGO = new GameObject($"Crack{i}");
                crackGO.transform.SetParent(canvas.transform, false);
                var crackImg = crackGO.AddComponent<Image>();
                crackImg.color = new Color(0.4f, 0.2f, 0.05f, 1f);

                var crackRT = crackImg.rectTransform;
                crackRT.anchorMin = new Vector2(0.5f, 0.5f);
                crackRT.anchorMax = new Vector2(0.5f, 0.5f);
                crackRT.pivot = new Vector2(0.5f, 0f);

                // 画面横幅全体に広がる亀裂
                float xOffset = (i - 12f) * 80f;
                crackRT.position = new Vector3(to.x + xOffset, -Screen.height / 2, 0);

                cracks.Add(crackRT);
            }

            // 150個の岩破片パーティクル
            var rockParticles = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 150; i++)
            {
                var particleGO = new GameObject($"RockDebris{i}");
                particleGO.transform.SetParent(canvas.transform, false);
                var particleText = particleGO.AddComponent<Text>();
                particleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                particleText.fontSize = UnityEngine.Random.Range(40, 80);
                particleText.text = "岩";
                particleText.color = new Color(0.5f, 0.3f, 0.1f, 1f);
                particleText.alignment = TextAnchor.MiddleCenter;

                var particleRT = particleText.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(60, 60);
                particleRT.position = to;

                // 地面から飛び散る
                float angle = UnityEngine.Random.Range(-90f, 90f) - 90f; // 上向き中心
                float speed = UnityEngine.Random.Range(500f, 1000f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                vel.y += 400f; // 重力に逆らって上昇

                rockParticles.Add((particleRT, vel));
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 超強力な画面揺れ（地震）
                float shake = Mathf.Sin(t * 60f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);

                // 茶色フラッシュ（大地の怒り）
                float alpha = t < 0.2f ? Mathf.Lerp(0f, 0.7f, t / 0.2f) : Mathf.Lerp(0.7f, 0f, (t - 0.2f) / 0.8f);
                flashImg.color = new Color(0.5f, 0.3f, 0.1f, alpha);

                // 亀裂の成長（画面全体を突き破る）
                for (int i = 0; i < cracks.Count; i++)
                {
                    float growSpeed = t < 0.3f ? Mathf.Pow(t / 0.3f, 0.5f) : 1f;
                    float height = Mathf.Lerp(0f, Screen.height * 1.5f, growSpeed); // 画面の1.5倍の高さ
                    float width = UnityEngine.Random.Range(20f, 50f); // 3倍太い

                    cracks[i].sizeDelta = new Vector2(width, height);

                    // 亀裂の透明度
                    var img = cracks[i].GetComponent<Image>();
                    Color c = img.color;
                    c.a = t < 0.7f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.7f) / 0.3f);
                    img.color = c;
                }

                // 岩破片の物理演算
                foreach (var (rt, velocity) in rockParticles)
                {
                    if (rt != null)
                    {
                        // 重力
                        Vector2 newVel = velocity - new Vector2(0, 800f * Time.deltaTime);
                        rt.position += (Vector3)(newVel * Time.deltaTime);

                        // フェードアウト
                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = 1f - t;
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var crack in cracks) Destroy(crack.gameObject);
            foreach (var (rt, _) in rockParticles)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        System.Collections.IEnumerator Effect_ClayGolem(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大岩石落下：天から降り注ぐ巨大隕石の雨
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 25f;
            float duration = 2.0f;
            float elapsed = 0f;

            // 30個の巨大岩石（隕石サイズ）
            var rocks = new List<(RectTransform rt, Vector3 startPos, Vector3 endPos, float fallSpeed, float rotationSpeed)>();
            for (int i = 0; i < 30; i++)
            {
                var rockGO = new GameObject($"MeteorRock{i}");
                rockGO.transform.SetParent(canvas.transform, false);
                var rockText = rockGO.AddComponent<Text>();
                rockText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                rockText.fontSize = UnityEngine.Random.Range(80, 160); // 巨大化
                rockText.text = "岩";
                rockText.fontStyle = FontStyle.Bold;
                rockText.color = new Color(0.5f, 0.3f, 0.15f, 1f);
                rockText.alignment = TextAnchor.MiddleCenter;

                var rockRT = rockText.rectTransform;
                rockRT.anchorMin = new Vector2(0.5f, 0.5f);
                rockRT.anchorMax = new Vector2(0.5f, 0.5f);
                rockRT.pivot = new Vector2(0.5f, 0.5f);
                rockRT.sizeDelta = new Vector2(120, 120); // 3倍サイズ

                // 画面上空から落下
                Vector3 startPos = to + new Vector3(UnityEngine.Random.Range(-500f, 500f), Screen.height, 0);
                Vector3 endPos = to + new Vector3(UnityEngine.Random.Range(-300f, 300f), -100f, 0);
                float fallSpeed = UnityEngine.Random.Range(0.4f, 0.8f);
                float rotationSpeed = UnityEngine.Random.Range(-360f, 360f);

                rockRT.position = startPos;
                rocks.Add((rockRT, startPos, endPos, fallSpeed, rotationSpeed));
            }

            // 100個の土煙パーティクル
            var dustParticles = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 100; i++)
            {
                var dustGO = new GameObject($"Dust{i}");
                dustGO.transform.SetParent(canvas.transform, false);
                var dustText = dustGO.AddComponent<Text>();
                dustText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                dustText.fontSize = UnityEngine.Random.Range(40, 70);
                dustText.text = "土";
                dustText.color = new Color(0.6f, 0.5f, 0.3f, 0.8f);
                dustText.alignment = TextAnchor.MiddleCenter;

                var dustRT = dustText.rectTransform;
                dustRT.anchorMin = new Vector2(0.5f, 0.5f);
                dustRT.anchorMax = new Vector2(0.5f, 0.5f);
                dustRT.pivot = new Vector2(0.5f, 0.5f);
                dustRT.sizeDelta = new Vector2(50, 50);
                dustRT.position = to;

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(200f, 500f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;

                dustParticles.Add((dustRT, vel));
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 激しい画面揺れ（隕石落下の衝撃）
                float shake = Mathf.Sin(t * 45f) * (1f - Mathf.Pow(t, 0.5f)) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.6f, 0);

                // 茶色フラッシュ（土煙）
                float alpha = t < 0.25f ? Mathf.Lerp(0f, 0.6f, t / 0.25f) : Mathf.Lerp(0.6f, 0f, (t - 0.25f) / 0.75f);
                flashImg.color = new Color(0.6f, 0.4f, 0.2f, alpha);

                // 岩石の落下と回転
                foreach (var (rt, startPos, endPos, fallSpeed, rotationSpeed) in rocks)
                {
                    if (rt != null)
                    {
                        float fallT = Mathf.Clamp01(t / fallSpeed);
                        rt.position = Vector3.Lerp(startPos, endPos, Mathf.Pow(fallT, 2f)); // 加速落下
                        rt.Rotate(0, 0, rotationSpeed * Time.deltaTime);

                        // 着地時にフェード
                        var txt = rt.GetComponent<Text>();
                        if (txt != null && fallT > 0.8f)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(1f, 0f, (fallT - 0.8f) / 0.2f);
                            txt.color = c;
                        }
                    }
                }

                // 土煙拡散
                foreach (var (rt, velocity) in dustParticles)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(0.8f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var (rt, _, _, _, _) in rocks)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var (rt, _) in dustParticles)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        System.Collections.IEnumerator Effect_StoneBoar(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大石猪突進：画面を揺らす荒々しい突撃
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 22f;
            float duration = 1.4f;
            float elapsed = 0f;

            // 巨大猪本体
            var boarGO = new GameObject("GiantBoar");
            boarGO.transform.SetParent(canvas.transform, false);
            var boarText = boarGO.AddComponent<Text>();
            boarText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            boarText.fontSize = 200; // 60→200に超巨大化
            boarText.fontStyle = FontStyle.Bold;
            boarText.text = "猪";
            boarText.color = new Color(0.4f, 0.3f, 0.2f, 1f);
            boarText.alignment = TextAnchor.MiddleCenter;

            var boarRT = boarText.rectTransform;
            boarRT.anchorMin = new Vector2(0.5f, 0.5f);
            boarRT.anchorMax = new Vector2(0.5f, 0.5f);
            boarRT.pivot = new Vector2(0.5f, 0.5f);
            boarRT.sizeDelta = new Vector2(250, 250);
            boarRT.position = from;

            // 突進の軌道上に土煙80個
            var dustTrail = new List<(RectTransform rt, Vector2 velocity, float spawnTime)>();
            for (int i = 0; i < 80; i++)
            {
                var dustGO = new GameObject($"DustTrail{i}");
                dustGO.transform.SetParent(canvas.transform, false);
                var dustText = dustGO.AddComponent<Text>();
                dustText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                dustText.fontSize = UnityEngine.Random.Range(50, 90);
                dustText.text = "土";
                dustText.color = new Color(0.6f, 0.5f, 0.3f, 0.9f);
                dustText.alignment = TextAnchor.MiddleCenter;

                var dustRT = dustText.rectTransform;
                dustRT.anchorMin = new Vector2(0.5f, 0.5f);
                dustRT.anchorMax = new Vector2(0.5f, 0.5f);
                dustRT.pivot = new Vector2(0.5f, 0.5f);
                dustRT.sizeDelta = new Vector2(60, 60);

                float spawnTime = UnityEngine.Random.Range(0f, 0.7f);
                Vector2 vel = new Vector2(UnityEngine.Random.Range(-300f, 300f), UnityEngine.Random.Range(200f, 500f));

                dustTrail.Add((dustRT, vel, spawnTime));
            }

            // 衝突時の石破片100個
            var impactRocks = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 100; i++)
            {
                var rockGO = new GameObject($"ImpactRock{i}");
                rockGO.transform.SetParent(canvas.transform, false);
                var rockText = rockGO.AddComponent<Text>();
                rockText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                rockText.fontSize = UnityEngine.Random.Range(35, 65);
                rockText.text = "石";
                rockText.color = new Color(0.5f, 0.4f, 0.3f, 1f);
                rockText.alignment = TextAnchor.MiddleCenter;

                var rockRT = rockText.rectTransform;
                rockRT.anchorMin = new Vector2(0.5f, 0.5f);
                rockRT.anchorMax = new Vector2(0.5f, 0.5f);
                rockRT.pivot = new Vector2(0.5f, 0.5f);
                rockRT.sizeDelta = new Vector2(50, 50);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(400f, 800f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;

                impactRocks.Add((rockRT, vel));
            }

            bool impactTriggered = false;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 猪の突進移動（加速）
                float moveT = Mathf.Pow(t, 0.7f);
                boarRT.position = Vector3.Lerp(from, to, moveT);

                // 突進の振動（縦揺れ強調）
                float shake = Mathf.Sin(t * 70f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake * 0.3f, shake, 0);

                // 茶色フラッシュ
                float alpha = t < 0.15f ? Mathf.Lerp(0f, 0.5f, t / 0.15f) : Mathf.Lerp(0.5f, 0f, (t - 0.15f) / 0.85f);
                flashImg.color = new Color(0.5f, 0.4f, 0.3f, alpha);

                // 土煙発生
                foreach (var (rt, velocity, spawnTime) in dustTrail)
                {
                    if (rt != null && elapsed >= spawnTime)
                    {
                        if (rt.position.x == 0 && rt.position.y == 0) rt.position = boarRT.position;
                        rt.position += (Vector3)(velocity * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Max(0, 0.9f - (elapsed - spawnTime) * 1.5f);
                            txt.color = c;
                        }
                    }
                }

                // 衝突判定（到達時に爆発）
                if (moveT > 0.85f && !impactTriggered)
                {
                    impactTriggered = true;
                    foreach (var (rt, _) in impactRocks)
                    {
                        if (rt != null) rt.position = to;
                    }
                }

                // 衝突後の石破片飛散
                if (impactTriggered)
                {
                    foreach (var (rt, velocity) in impactRocks)
                    {
                        if (rt != null)
                        {
                            rt.position += (Vector3)(velocity * Time.deltaTime);

                            var txt = rt.GetComponent<Text>();
                            if (txt != null)
                            {
                                Color c = txt.color;
                                c.a = Mathf.Lerp(1f, 0f, (elapsed - 0.85f * duration) / (duration * 0.15f));
                                txt.color = c;
                            }
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            Destroy(boarGO);
            foreach (var (rt, _, _) in dustTrail)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var (rt, _) in impactRocks)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        System.Collections.IEnumerator Effect_GraniteRhino(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大地震：画面を破壊する大地の怒り
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 40f; // 最大級の地震
            float duration = 2.2f;
            float elapsed = 0f;

            // 画面中央に巨大な「震」文字
            var quakeTextGO = new GameObject("QuakeText");
            quakeTextGO.transform.SetParent(canvas.transform, false);
            var quakeText = quakeTextGO.AddComponent<Text>();
            quakeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            quakeText.fontSize = 300;
            quakeText.fontStyle = FontStyle.Bold;
            quakeText.text = "震";
            quakeText.color = new Color(0.5f, 0.3f, 0.1f, 1f);
            quakeText.alignment = TextAnchor.MiddleCenter;

            var quakeTextRT = quakeText.rectTransform;
            quakeTextRT.anchorMin = new Vector2(0.5f, 0.5f);
            quakeTextRT.anchorMax = new Vector2(0.5f, 0.5f);
            quakeTextRT.pivot = new Vector2(0.5f, 0.5f);
            quakeTextRT.sizeDelta = new Vector2(400, 400);
            quakeTextRT.position = to;

            // 120個の跳ねる岩石
            var quakeRocks = new List<(RectTransform rt, Vector2 velocity, Vector3 startPos)>();
            for (int i = 0; i < 120; i++)
            {
                var rockGO = new GameObject($"QuakeRock{i}");
                rockGO.transform.SetParent(canvas.transform, false);
                var rockText = rockGO.AddComponent<Text>();
                rockText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                rockText.fontSize = UnityEngine.Random.Range(50, 100);
                rockText.text = "石";
                rockText.fontStyle = FontStyle.Bold;
                rockText.color = new Color(0.5f, 0.4f, 0.3f, 1f);
                rockText.alignment = TextAnchor.MiddleCenter;

                var rockRT = rockText.rectTransform;
                rockRT.anchorMin = new Vector2(0.5f, 0.5f);
                rockRT.anchorMax = new Vector2(0.5f, 0.5f);
                rockRT.pivot = new Vector2(0.5f, 0.5f);
                rockRT.sizeDelta = new Vector2(70, 70);

                // 画面全体からランダムに配置
                Vector3 startPos = new Vector3(
                    UnityEngine.Random.Range(-Screen.width / 2, Screen.width / 2),
                    UnityEngine.Random.Range(-Screen.height / 2, Screen.height / 2),
                    0
                );
                rockRT.position = startPos;

                // 地震で跳ね上がる
                Vector2 vel = new Vector2(
                    UnityEngine.Random.Range(-400f, 400f),
                    UnityEngine.Random.Range(600f, 1200f)
                );

                quakeRocks.Add((rockRT, vel, startPos));
            }

            // 地割れ波（画面横断）
            var crackWaves = new List<RectTransform>();
            for (int i = 0; i < 15; i++)
            {
                var waveGO = new GameObject($"CrackWave{i}");
                waveGO.transform.SetParent(canvas.transform, false);
                var waveImg = waveGO.AddComponent<Image>();
                waveImg.color = new Color(0.4f, 0.2f, 0.1f, 0.8f);

                var waveRT = waveImg.rectTransform;
                waveRT.anchorMin = new Vector2(0.5f, 0.5f);
                waveRT.anchorMax = new Vector2(0.5f, 0.5f);
                waveRT.pivot = new Vector2(0.5f, 0.5f);

                float yPos = UnityEngine.Random.Range(-Screen.height / 2, Screen.height / 2);
                waveRT.position = new Vector3(-Screen.width / 2 - 100, yPos, 0);

                crackWaves.Add(waveRT);
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 超強力な縦横ランダム地震
                float shakeX = Mathf.Sin(t * 75f) * Mathf.Cos(t * 40f) * (1f - t) * shakeIntensity;
                float shakeY = Mathf.Cos(t * 60f) * Mathf.Sin(t * 50f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shakeX, shakeY, 0);

                // 濃い茶色フラッシュ（大地の崩壊）
                float alpha = t < 0.2f ? Mathf.Lerp(0f, 0.8f, t / 0.2f) : Mathf.Lerp(0.8f, 0f, (t - 0.2f) / 0.8f);
                flashImg.color = new Color(0.5f, 0.3f, 0.1f, alpha);

                // 「震」文字の振動と拡大
                float quakeScale = 1f + Mathf.Sin(t * Mathf.PI * 3f) * 0.3f;
                quakeTextRT.localScale = Vector3.one * quakeScale;
                quakeText.color = new Color(0.5f, 0.3f, 0.1f, Mathf.Sin(t * Mathf.PI));

                // 岩石の物理演算（跳ねる）
                foreach (var (rt, velocity, startPos) in quakeRocks)
                {
                    if (rt != null)
                    {
                        // 重力
                        Vector2 newVel = velocity - new Vector2(0, 1500f * Time.deltaTime);
                        rt.position += (Vector3)(newVel * Time.deltaTime);

                        // 地面反発（バウンド）
                        if (rt.position.y < -Screen.height / 2)
                        {
                            Vector3 pos = rt.position;
                            pos.y = -Screen.height / 2;
                            rt.position = pos;
                        }

                        // フェード
                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = 1f - t;
                            txt.color = c;
                        }
                    }
                }

                // 地割れ波が画面を横断
                for (int i = 0; i < crackWaves.Count; i++)
                {
                    var waveRT = crackWaves[i];
                    if (waveRT != null)
                    {
                        float waveT = Mathf.Clamp01(t * 1.5f - i * 0.05f);
                        float xPos = Mathf.Lerp(-Screen.width / 2 - 100, Screen.width / 2 + 100, waveT);
                        waveRT.position = new Vector3(xPos, waveRT.position.y, 0);
                        waveRT.sizeDelta = new Vector2(40, UnityEngine.Random.Range(300f, 600f));

                        var img = waveRT.GetComponent<Image>();
                        if (img != null)
                        {
                            Color c = img.color;
                            c.a = Mathf.Sin(waveT * Mathf.PI) * 0.8f;
                            img.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            Destroy(quakeTextGO);
            foreach (var (rt, _, _) in quakeRocks)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var waveRT in crackWaves)
            {
                if (waveRT != null) Destroy(waveRT.gameObject);
            }
        }

        System.Collections.IEnumerator Effect_PebbleSprite(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超大量小石マシンガン：砂嵐のような弾幕
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 15f;
            float duration = 1.5f;
            float elapsed = 0f;

            // 150個の小石弾幕
            var pebbles = new List<(RectTransform rt, Vector3 startPos, Vector3 endPos, float speed)>();
            for (int i = 0; i < 150; i++)
            {
                var pebbleGO = new GameObject($"Pebble{i}");
                pebbleGO.transform.SetParent(canvas.transform, false);
                var pebbleImg = pebbleGO.AddComponent<Image>();
                pebbleImg.color = new Color(0.55f, 0.45f, 0.35f, 1f);

                var pebbleRT = pebbleImg.rectTransform;
                pebbleRT.anchorMin = new Vector2(0.5f, 0.5f);
                pebbleRT.anchorMax = new Vector2(0.5f, 0.5f);
                pebbleRT.pivot = new Vector2(0.5f, 0.5f);
                pebbleRT.sizeDelta = new Vector2(25, 25); // 10→25に拡大

                Vector3 spread = new Vector3(
                    UnityEngine.Random.Range(-300f, 300f),
                    UnityEngine.Random.Range(-200f, 200f),
                    0
                );
                Vector3 startPos = from;
                Vector3 endPos = to + spread;
                float speed = UnityEngine.Random.Range(800f, 1400f);

                pebbleRT.position = startPos;
                pebbles.Add((pebbleRT, startPos, endPos, speed));
            }

            // 80個の砂塵パーティクル
            var dustCloud = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 80; i++)
            {
                var dustGO = new GameObject($"SandDust{i}");
                dustGO.transform.SetParent(canvas.transform, false);
                var dustText = dustGO.AddComponent<Text>();
                dustText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                dustText.fontSize = UnityEngine.Random.Range(30, 60);
                dustText.text = "砂";
                dustText.color = new Color(0.6f, 0.5f, 0.4f, 0.7f);
                dustText.alignment = TextAnchor.MiddleCenter;

                var dustRT = dustText.rectTransform;
                dustRT.anchorMin = new Vector2(0.5f, 0.5f);
                dustRT.anchorMax = new Vector2(0.5f, 0.5f);
                dustRT.pivot = new Vector2(0.5f, 0.5f);
                dustRT.sizeDelta = new Vector2(40, 40);
                dustRT.position = from;

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(200f, 400f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;

                dustCloud.Add((dustRT, vel));
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 画面揺れ（小刻み）
                float shake = Mathf.Sin(t * 80f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.3f, 0);

                // 砂色フラッシュ
                float alpha = t < 0.15f ? Mathf.Lerp(0f, 0.4f, t / 0.15f) : Mathf.Lerp(0.4f, 0f, (t - 0.15f) / 0.85f);
                flashImg.color = new Color(0.6f, 0.5f, 0.4f, alpha);

                // 小石の移動
                foreach (var (rt, startPos, endPos, speed) in pebbles)
                {
                    if (rt != null)
                    {
                        Vector3 dir = (endPos - startPos).normalized;
                        rt.position += dir * speed * Time.deltaTime;

                        // 回転
                        rt.Rotate(0, 0, speed * 0.5f * Time.deltaTime);

                        // フェード
                        var img = rt.GetComponent<Image>();
                        if (img != null)
                        {
                            Color c = img.color;
                            c.a = 1f - t;
                            img.color = c;
                        }
                    }
                }

                // 砂塵拡散
                foreach (var (rt, velocity) in dustCloud)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(0.7f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var (rt, _, _, _) in pebbles)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var (rt, _) in dustCloud)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        // ========== Air属性演出 ==========

        System.Collections.IEnumerator Effect_WindHawk(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大風の刃：画面を切り裂く真空波
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 18f;
            float duration = 1.3f;
            float elapsed = 0f;

            // 18本の巨大風刃
            var blades = new List<(RectTransform rt, Vector3 startPos, Vector3 endPos, float delay)>();
            for (int i = 0; i < 18; i++)
            {
                var bladeGO = new GameObject($"WindBlade{i}");
                bladeGO.transform.SetParent(canvas.transform, false);
                var bladeImg = bladeGO.AddComponent<Image>();
                bladeImg.color = new Color(0.7f, 1f, 1f, 0.9f);

                var bladeRT = bladeImg.rectTransform;
                bladeRT.anchorMin = new Vector2(0.5f, 0.5f);
                bladeRT.anchorMax = new Vector2(0.5f, 0.5f);
                bladeRT.pivot = new Vector2(0.5f, 0.5f);
                bladeRT.sizeDelta = new Vector2(250, 20); // 60x5 → 250x20に巨大化

                float angle = UnityEngine.Random.Range(-45f, 45f);
                bladeRT.rotation = Quaternion.Euler(0, 0, angle);

                Vector3 offset = new Vector3(0, i * 50f - 400f, 0);
                Vector3 startPos = from + offset;
                Vector3 endPos = to + offset;
                float delay = i * 0.04f;

                bladeRT.position = startPos;
                blades.Add((bladeRT, startPos, endPos, delay));
            }

            // 100個の風パーティクル
            var windParticles = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 100; i++)
            {
                var particleGO = new GameObject($"WindParticle{i}");
                particleGO.transform.SetParent(canvas.transform, false);
                var particleText = particleGO.AddComponent<Text>();
                particleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                particleText.fontSize = UnityEngine.Random.Range(35, 65);
                particleText.text = "風";
                particleText.color = new Color(0.7f, 0.95f, 1f, 0.9f);
                particleText.alignment = TextAnchor.MiddleCenter;

                var particleRT = particleText.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(50, 50);
                particleRT.position = from;

                // 刃と一緒に飛ぶ
                Vector2 vel = new Vector2(UnityEngine.Random.Range(600f, 1000f), UnityEngine.Random.Range(-200f, 200f));
                windParticles.Add((particleRT, vel));
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 鋭い画面揺れ（切り裂く感覚）
                float shake = Mathf.Sin(t * 90f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake * 0.2f, shake, 0);

                // 青白いフラッシュ（真空波）
                float alpha = t < 0.12f ? Mathf.Lerp(0f, 0.5f, t / 0.12f) : Mathf.Lerp(0.5f, 0f, (t - 0.12f) / 0.88f);
                flashImg.color = new Color(0.7f, 1f, 1f, alpha);

                // 風刃の移動
                foreach (var (rt, startPos, endPos, delay) in blades)
                {
                    if (rt != null && elapsed >= delay)
                    {
                        float bladeT = Mathf.Clamp01((elapsed - delay) / (duration - delay));
                        rt.position = Vector3.Lerp(startPos, endPos, Mathf.Pow(bladeT, 0.6f)); // 加速

                        // フェード
                        var img = rt.GetComponent<Image>();
                        if (img != null)
                        {
                            Color c = img.color;
                            c.a = Mathf.Sin(bladeT * Mathf.PI) * 0.9f;
                            img.color = c;
                        }
                    }
                }

                // 風パーティクル移動
                foreach (var (rt, velocity) in windParticles)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(0.9f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var (rt, _, _, _) in blades)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var (rt, _) in windParticles)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        System.Collections.IEnumerator Effect_GustFox(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大竜巻：画面を飲み込む暴風渦
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 20f;
            float duration = 1.8f;
            float elapsed = 0f;

            // 100個の風文字で構成される竜巻
            var winds = new List<(RectTransform rt, float angle, float height)>();
            for (int i = 0; i < 100; i++)
            {
                var windGO = new GameObject($"Wind{i}");
                windGO.transform.SetParent(canvas.transform, false);
                var windText = windGO.AddComponent<Text>();
                windText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                windText.fontSize = UnityEngine.Random.Range(50, 90); // 25→50-90に巨大化
                windText.text = "風";
                windText.fontStyle = FontStyle.Bold;
                windText.color = new Color(0.6f, 0.9f, 1f, 0.9f);
                windText.alignment = TextAnchor.MiddleCenter;

                var windRT = windText.rectTransform;
                windRT.anchorMin = new Vector2(0.5f, 0.5f);
                windRT.anchorMax = new Vector2(0.5f, 0.5f);
                windRT.pivot = new Vector2(0.5f, 0.5f);
                windRT.sizeDelta = new Vector2(70, 70);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float height = (float)i / 100f;

                winds.Add((windRT, angle, height));
            }

            // 80個の破片パーティクル（吸い込まれる）
            var debris = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 80; i++)
            {
                var debrisGO = new GameObject($"Debris{i}");
                debrisGO.transform.SetParent(canvas.transform, false);
                var debrisText = debrisGO.AddComponent<Text>();
                debrisText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                debrisText.fontSize = UnityEngine.Random.Range(30, 50);
                debrisText.text = "渦";
                debrisText.color = new Color(0.7f, 0.95f, 1f, 0.7f);
                debrisText.alignment = TextAnchor.MiddleCenter;

                var debrisRT = debrisText.rectTransform;
                debrisRT.anchorMin = new Vector2(0.5f, 0.5f);
                debrisRT.anchorMax = new Vector2(0.5f, 0.5f);
                debrisRT.pivot = new Vector2(0.5f, 0.5f);
                debrisRT.sizeDelta = new Vector2(45, 45);

                Vector2 vel = Vector2.zero;
                debris.Add((debrisRT, vel));
            }

            Vector3 tornadoCenter = (from + to) / 2f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 竜巻の揺れ（回転に合わせて）
                float shake = Mathf.Sin(elapsed * 10f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);

                // 青白いフラッシュ（竜巻の光）
                float alpha = t < 0.15f ? Mathf.Lerp(0f, 0.5f, t / 0.15f) : Mathf.Lerp(0.5f, 0f, (t - 0.15f) / 0.85f);
                flashImg.color = new Color(0.6f, 0.9f, 1f, alpha);

                // 竜巻の回転（高速化）
                for (int i = 0; i < winds.Count; i++)
                {
                    var wind = winds[i];
                    float rotationSpeed = 1800f; // 720→1800に高速化 (5回転/秒)
                    float currentAngle = wind.angle + elapsed * rotationSpeed;

                    // 半径を3倍に拡大 (80→240)
                    float baseRadius = 240f;
                    float radius = baseRadius * (1f - wind.height) * Mathf.Lerp(0.5f, 1.5f, t);

                    float x = Mathf.Cos(currentAngle * Mathf.Deg2Rad) * radius;
                    float y = Mathf.Lerp(-400f, 400f, wind.height); // -150~150 → -400~400に拡大

                    wind.rt.position = tornadoCenter + new Vector3(x, y, 0);

                    // 回転
                    wind.rt.Rotate(0, 0, rotationSpeed * Time.deltaTime);

                    var text = wind.rt.GetComponent<Text>();
                    Color c = text.color;
                    c.a = Mathf.Sin(t * Mathf.PI) * 0.9f;
                    text.color = c;
                }

                // 破片が竜巻に吸い込まれる
                for (int i = 0; i < debris.Count; i++)
                {
                    var (rt, _) = debris[i];
                    if (rt != null)
                    {
                        // ランダムな初期位置
                        if (elapsed < 0.1f)
                        {
                            rt.position = tornadoCenter + new Vector3(
                                UnityEngine.Random.Range(-400f, 400f),
                                UnityEngine.Random.Range(-300f, 300f),
                                0
                            );
                        }

                        // 中心に向かって吸い込まれる
                        Vector3 toCenter = tornadoCenter - rt.position;
                        rt.position += toCenter * Time.deltaTime * 2f;

                        // 螺旋回転
                        float spiralAngle = elapsed * 720f + i * 10f;
                        Vector3 spiralOffset = new Vector3(
                            Mathf.Cos(spiralAngle * Mathf.Deg2Rad) * 50f,
                            Mathf.Sin(spiralAngle * Mathf.Deg2Rad) * 50f,
                            0
                        );
                        rt.position += spiralOffset * Time.deltaTime;

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(0.7f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var wind in winds) Destroy(wind.rt.gameObject);
            foreach (var (rt, _) in debris)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        System.Collections.IEnumerator Effect_SkyGolem(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大暴風：画面全体を吹き飛ばす猛烈な風
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 28f;
            float duration = 1.9f;
            float elapsed = 0f;

            // 120本の暴風線
            var gusts = new List<(RectTransform rt, Vector3 startPos, float speed, float yOffset)>();
            for (int i = 0; i < 120; i++)
            {
                var gustGO = new GameObject($"Gust{i}");
                gustGO.transform.SetParent(canvas.transform, false);
                var gustImg = gustGO.AddComponent<Image>();
                gustImg.color = new Color(0.8f, 1f, 1f, 0.8f);

                var gustRT = gustImg.rectTransform;
                gustRT.anchorMin = new Vector2(0.5f, 0.5f);
                gustRT.anchorMax = new Vector2(0.5f, 0.5f);
                gustRT.pivot = new Vector2(0.5f, 0.5f);
                // 3倍サイズ (30-60 → 90-180、3-8 → 15-30)
                gustRT.sizeDelta = new Vector2(UnityEngine.Random.Range(90f, 180f), UnityEngine.Random.Range(15f, 30f));

                Vector3 startPos = from + new Vector3(
                    UnityEngine.Random.Range(-150f, 150f),
                    UnityEngine.Random.Range(-300f, 300f),
                    0
                );

                float speed = UnityEngine.Random.Range(1000f, 1800f);
                float yOffset = UnityEngine.Random.Range(-50f, 50f);

                gustRT.position = startPos;
                gusts.Add((gustRT, startPos, speed, yOffset));
            }

            // 100個の風圧パーティクル
            var windPressure = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 100; i++)
            {
                var pressureGO = new GameObject($"Pressure{i}");
                pressureGO.transform.SetParent(canvas.transform, false);
                var pressureText = pressureGO.AddComponent<Text>();
                pressureText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                pressureText.fontSize = UnityEngine.Random.Range(50, 90);
                pressureText.text = "風";
                pressureText.fontStyle = FontStyle.Bold;
                pressureText.color = new Color(0.7f, 0.95f, 1f, 0.8f);
                pressureText.alignment = TextAnchor.MiddleCenter;

                var pressureRT = pressureText.rectTransform;
                pressureRT.anchorMin = new Vector2(0.5f, 0.5f);
                pressureRT.anchorMax = new Vector2(0.5f, 0.5f);
                pressureRT.pivot = new Vector2(0.5f, 0.5f);
                pressureRT.sizeDelta = new Vector2(70, 70);
                pressureRT.position = from;

                Vector2 vel = new Vector2(UnityEngine.Random.Range(800f, 1400f), UnityEngine.Random.Range(-200f, 200f));
                windPressure.Add((pressureRT, vel));
            }

            Vector3 direction = isPlayer ? Vector3.right : Vector3.left;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 横方向の強烈な揺れ（暴風）
                float shake = Mathf.Sin(t * 80f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.3f, 0);

                // 白青フラッシュ（強風の光）
                float alpha = t < 0.2f ? Mathf.Lerp(0f, 0.6f, t / 0.2f) : Mathf.Lerp(0.6f, 0f, (t - 0.2f) / 0.8f);
                flashImg.color = new Color(0.8f, 1f, 1f, alpha);

                // 暴風線の移動
                foreach (var (rt, startPos, speed, yOffset) in gusts)
                {
                    if (rt != null)
                    {
                        rt.position += direction * speed * Time.deltaTime;
                        rt.position += new Vector3(0, Mathf.Sin(elapsed * 10f + yOffset) * 3f, 0);

                        // フェード
                        var img = rt.GetComponent<Image>();
                        if (img != null)
                        {
                            Color c = img.color;
                            c.a = Mathf.Lerp(0.8f, 0f, t);
                            img.color = c;
                        }
                    }
                }

                // 風圧パーティクル
                foreach (var (rt, velocity) in windPressure)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(0.8f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var (rt, _, _, _) in gusts)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var (rt, _) in windPressure)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        System.Collections.IEnumerator Effect_WhirlPixie(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超高速旋風群：複数の小型竜巻が乱舞
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 12f;
            float duration = 1.2f;
            float elapsed = 0f;

            // 60個の旋風パーティクル（複数の渦）
            var whirls = new List<(RectTransform rt, int group, float baseAngle)>();
            for (int i = 0; i < 60; i++)
            {
                var whirlGO = new GameObject($"Whirl{i}");
                whirlGO.transform.SetParent(canvas.transform, false);
                var whirlImg = whirlGO.AddComponent<Image>();
                whirlImg.color = new Color(0.7f, 0.95f, 1f, 0.85f);

                var whirlRT = whirlImg.rectTransform;
                whirlRT.anchorMin = new Vector2(0.5f, 0.5f);
                whirlRT.anchorMax = new Vector2(0.5f, 0.5f);
                whirlRT.pivot = new Vector2(0.5f, 0.5f);
                whirlRT.sizeDelta = new Vector2(40, 40); // 15→40に拡大

                int group = i / 20; // 3グループに分割
                float baseAngle = UnityEngine.Random.Range(0f, 360f);

                whirls.Add((whirlRT, group, baseAngle));
            }

            // 90個の風の軌跡
            var trails = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 90; i++)
            {
                var trailGO = new GameObject($"Trail{i}");
                trailGO.transform.SetParent(canvas.transform, false);
                var trailText = trailGO.AddComponent<Text>();
                trailText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                trailText.fontSize = UnityEngine.Random.Range(25, 50);
                trailText.text = "旋";
                trailText.color = new Color(0.6f, 0.9f, 1f, 0.7f);
                trailText.alignment = TextAnchor.MiddleCenter;

                var trailRT = trailText.rectTransform;
                trailRT.anchorMin = new Vector2(0.5f, 0.5f);
                trailRT.anchorMax = new Vector2(0.5f, 0.5f);
                trailRT.pivot = new Vector2(0.5f, 0.5f);
                trailRT.sizeDelta = new Vector2(40, 40);
                trailRT.position = from;

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(300f, 600f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;

                trails.Add((trailRT, vel));
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 小刻みな揺れ（複数の渦の干渉）
                float shake = Mathf.Sin(t * 100f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake * 0.7f, shake, 0);

                // 淡い青白フラッシュ
                float alpha = t < 0.12f ? Mathf.Lerp(0f, 0.4f, t / 0.12f) : Mathf.Lerp(0.4f, 0f, (t - 0.12f) / 0.88f);
                flashImg.color = new Color(0.7f, 0.95f, 1f, alpha);

                // 複数の旋風が回転（3つの中心）
                for (int i = 0; i < whirls.Count; i++)
                {
                    var (rt, group, baseAngle) = whirls[i];
                    if (rt != null)
                    {
                        // 各グループの中心位置
                        Vector3 groupCenter = Vector3.zero;
                        if (group == 0) groupCenter = from;
                        else if (group == 1) groupCenter = (from + to) / 2f;
                        else groupCenter = to;

                        // 超高速回転 (900 → 2700)
                        float angle = baseAngle + elapsed * 2700f;
                        float radius = Mathf.Lerp(50f, 180f, Mathf.Sin(t * Mathf.PI)); // 膨張収縮

                        float x = Mathf.Cos(angle * Mathf.Deg2Rad) * radius;
                        float y = Mathf.Sin(angle * Mathf.Deg2Rad) * radius;

                        rt.position = groupCenter + new Vector3(x, y, 0);

                        // 回転
                        rt.Rotate(0, 0, 2700f * Time.deltaTime);

                        var img = rt.GetComponent<Image>();
                        Color c = img.color;
                        c.a = Mathf.Sin(t * Mathf.PI) * 0.85f;
                        img.color = c;
                    }
                }

                // 軌跡パーティクル
                foreach (var (rt, velocity) in trails)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);
                        rt.Rotate(0, 0, 720f * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(0.7f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var (rt, _, _) in whirls)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var (rt, _) in trails)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        System.Collections.IEnumerator Effect_StormDrake(Vector3 from, Vector3 to, bool isPlayer)
        {
            // 超巨大雷嵐：画面を覆う暗雲と乱れ撃つ雷撃
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            Vector3 originalCanvasPos = canvas.transform.position;
            float shakeIntensity = 30f;
            float duration = 2.0f;
            float elapsed = 0f;

            // 巨大雷雲 (5個)
            var clouds = new List<RectTransform>();
            for (int i = 0; i < 5; i++)
            {
                var cloudGO = new GameObject($"Cloud{i}");
                cloudGO.transform.SetParent(canvas.transform, false);
                var cloudText = cloudGO.AddComponent<Text>();
                cloudText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                cloudText.fontSize = UnityEngine.Random.Range(150, 250); // 80→150-250に巨大化
                cloudText.text = "雲";
                cloudText.fontStyle = FontStyle.Bold;
                cloudText.color = new Color(0.3f, 0.3f, 0.4f, 0.9f);
                cloudText.alignment = TextAnchor.MiddleCenter;

                var cloudRT = cloudText.rectTransform;
                cloudRT.anchorMin = new Vector2(0.5f, 0.5f);
                cloudRT.anchorMax = new Vector2(0.5f, 0.5f);
                cloudRT.pivot = new Vector2(0.5f, 0.5f);
                cloudRT.sizeDelta = new Vector2(300, 200);
                cloudRT.position = new Vector3(
                    UnityEngine.Random.Range(-300f, 300f),
                    Screen.height / 2 + 100,
                    0
                );

                clouds.Add(cloudRT);
            }

            // 30本の稲妻
            var bolts = new List<(RectTransform rt, float spawnTime, Vector3 startPos, Vector3 endPos)>();
            for (int i = 0; i < 30; i++)
            {
                var boltGO = new GameObject($"Bolt{i}");
                boltGO.transform.SetParent(canvas.transform, false);
                var boltImg = boltGO.AddComponent<Image>();
                boltImg.color = new Color(1f, 1f, 0.5f, 1f);

                var boltRT = boltImg.rectTransform;
                boltRT.anchorMin = new Vector2(0.5f, 0.5f);
                boltRT.anchorMax = new Vector2(0.5f, 0.5f);
                boltRT.pivot = new Vector2(0.5f, 1f);

                // サイズ: 8x200 → 30x600に巨大化
                boltRT.sizeDelta = new Vector2(30, 600);

                float spawnTime = i * 0.05f;
                Vector3 startPos = new Vector3(
                    UnityEngine.Random.Range(-400f, 400f),
                    Screen.height / 2,
                    0
                );
                Vector3 endPos = startPos - new Vector3(0, 600, 0);

                bolts.Add((boltRT, spawnTime, startPos, endPos));
            }

            // 80個の雷パーティクル
            var thunderParticles = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 80; i++)
            {
                var particleGO = new GameObject($"Thunder{i}");
                particleGO.transform.SetParent(canvas.transform, false);
                var particleText = particleGO.AddComponent<Text>();
                particleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                particleText.fontSize = UnityEngine.Random.Range(40, 70);
                particleText.text = "雷";
                particleText.color = new Color(1f, 1f, 0.6f, 0.9f);
                particleText.alignment = TextAnchor.MiddleCenter;

                var particleRT = particleText.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(60, 60);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(300f, 600f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;

                thunderParticles.Add((particleRT, vel));
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 雷鳴の激しい揺れ
                float shake = (UnityEngine.Random.value > 0.9f ? 1.5f : 1f) * Mathf.Sin(t * 70f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.4f, 0);

                // 黄色フラッシュ（雷光）- ランダムに強烈に光る
                float flashIntensity = UnityEngine.Random.value > 0.85f ? 0.9f : 0.5f;
                float alpha = t < 0.1f ? Mathf.Lerp(0f, flashIntensity, t / 0.1f) : Mathf.Lerp(flashIntensity, 0f, (t - 0.1f) / 0.9f);
                flashImg.color = new Color(1f, 1f, 0.7f, alpha * (UnityEngine.Random.value > 0.7f ? 1f : 0.5f));

                // 雲の移動と脈動
                foreach (var cloudRT in clouds)
                {
                    if (cloudRT != null)
                    {
                        // ゆっくり漂う
                        cloudRT.position += new Vector3(Mathf.Sin(elapsed * 2f) * 0.5f, 0, 0);

                        // 脈動
                        float scale = 1f + Mathf.Sin(elapsed * 5f) * 0.1f;
                        cloudRT.localScale = Vector3.one * scale;

                        var txt = cloudRT.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = 0.9f * Mathf.Sin(t * Mathf.PI);
                            txt.color = c;
                        }
                    }
                }

                // 稲妻の出現と点滅
                foreach (var (rt, spawnTime, startPos, endPos) in bolts)
                {
                    if (rt != null)
                    {
                        if (elapsed >= spawnTime && elapsed < spawnTime + 0.3f)
                        {
                            rt.position = startPos;
                            rt.gameObject.SetActive(true);

                            // 激しく点滅
                            var img = rt.GetComponent<Image>();
                            if (img != null)
                            {
                                float boltT = (elapsed - spawnTime) / 0.3f;
                                bool visible = UnityEngine.Random.value > 0.3f;
                                Color c = img.color;
                                c.a = visible ? Mathf.Lerp(1f, 0f, boltT) : 0f;
                                img.color = c;
                            }
                        }
                        else
                        {
                            rt.gameObject.SetActive(false);
                        }
                    }
                }

                // 雷パーティクル
                foreach (var (rt, velocity) in thunderParticles)
                {
                    if (rt != null)
                    {
                        if (elapsed < 0.2f)
                        {
                            rt.position = new Vector3(
                                UnityEngine.Random.Range(-300f, 300f),
                                UnityEngine.Random.Range(-200f, 200f),
                                0
                            );
                        }

                        rt.position += (Vector3)(velocity * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(0.9f, 0f, t) * (UnityEngine.Random.value > 0.5f ? 1f : 0.3f);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var cloudRT in clouds)
            {
                if (cloudRT != null) Destroy(cloudRT.gameObject);
            }
            foreach (var (rt, _, _, _) in bolts)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
            foreach (var (rt, _) in thunderParticles)
            {
                if (rt != null) Destroy(rt.gameObject);
            }
        }

        // ========== ゲーム終了演出 ==========

        /// <summary>
        /// 不戦敗専用の超派手演出
        /// </summary>
        System.Collections.IEnumerator ShowForfeitEffect()
        {
            // 画面全体を覆う警告背景
            var bgPanel = new GameObject("ForfeitBG");
            bgPanel.transform.SetParent(canvas.transform, false);
            var bgImg = bgPanel.AddComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0);

            var bgRT = bgImg.rectTransform;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            Vector3 originalCanvasPos = canvas.transform.position;

            // 警告フラッシュ
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // 「不戦敗」テキスト
            var forfeitTextGO = new GameObject("ForfeitText");
            forfeitTextGO.transform.SetParent(bgPanel.transform, false);
            var forfeitTxt = forfeitTextGO.AddComponent<Text>();
            forfeitTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            forfeitTxt.fontSize = 1;
            forfeitTxt.fontStyle = FontStyle.Bold;
            forfeitTxt.alignment = TextAnchor.MiddleCenter;
            forfeitTxt.color = new Color(1f, 0.2f, 0.2f, 1f);
            forfeitTxt.text = "不戦敗";

            var forfeitRT = forfeitTxt.rectTransform;
            forfeitRT.anchorMin = new Vector2(0.5f, 0.6f);
            forfeitRT.anchorMax = new Vector2(0.5f, 0.6f);
            forfeitRT.pivot = new Vector2(0.5f, 0.5f);
            forfeitRT.sizeDelta = new Vector2(800, 300);

            // 「コスト不足」テキスト
            var reasonTextGO = new GameObject("ReasonText");
            reasonTextGO.transform.SetParent(bgPanel.transform, false);
            var reasonTxt = reasonTextGO.AddComponent<Text>();
            reasonTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            reasonTxt.fontSize = 50;
            reasonTxt.fontStyle = FontStyle.Bold;
            reasonTxt.alignment = TextAnchor.MiddleCenter;
            reasonTxt.color = new Color(1f, 0.5f, 0.3f, 0f);
            reasonTxt.text = "コスト不足\n出せるカードがありません";

            var reasonRT = reasonTxt.rectTransform;
            reasonRT.anchorMin = new Vector2(0.5f, 0.4f);
            reasonRT.anchorMax = new Vector2(0.5f, 0.4f);
            reasonRT.pivot = new Vector2(0.5f, 0.5f);
            reasonRT.sizeDelta = new Vector2(700, 150);

            // 200個のバツ印パーティクル
            var particles = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 200; i++)
            {
                var particleGO = new GameObject($"XParticle{i}");
                particleGO.transform.SetParent(bgPanel.transform, false);
                var particleTxt = particleGO.AddComponent<Text>();
                particleTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                particleTxt.fontSize = UnityEngine.Random.Range(50, 120);
                particleTxt.alignment = TextAnchor.MiddleCenter;
                particleTxt.fontStyle = FontStyle.Bold;
                particleTxt.text = "×";
                particleTxt.color = new Color(1f, 0.2f, 0.2f, 1f);

                var particleRT = particleTxt.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(100, 100);
                particleRT.position = new Vector3(0, 0, 0);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(300f, 800f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;

                particles.Add((particleRT, vel));
            }

            // 50個の「失格」文字
            var disqualifyTexts = new List<(RectTransform rt, Vector2 velocity, float rotSpeed)>();
            for (int i = 0; i < 50; i++)
            {
                var disqGO = new GameObject($"Disqualify{i}");
                disqGO.transform.SetParent(bgPanel.transform, false);
                var disqTxt = disqGO.AddComponent<Text>();
                disqTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                disqTxt.fontSize = UnityEngine.Random.Range(40, 80);
                disqTxt.alignment = TextAnchor.MiddleCenter;
                disqTxt.fontStyle = FontStyle.Bold;
                disqTxt.text = "失格";
                disqTxt.color = new Color(0.8f, 0.1f, 0.1f, 1f);

                var disqRT = disqTxt.rectTransform;
                disqRT.anchorMin = new Vector2(0.5f, 0.5f);
                disqRT.anchorMax = new Vector2(0.5f, 0.5f);
                disqRT.pivot = new Vector2(0.5f, 0.5f);
                disqRT.sizeDelta = new Vector2(80, 80);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(200f, 500f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
                float rotSpeed = UnityEngine.Random.Range(-360f, 360f);

                disqualifyTexts.Add((disqRT, vel, rotSpeed));
            }

            // メインアニメーション（2.5秒）
            float duration = 2.5f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 背景の暗転
                bgImg.color = new Color(0, 0, 0, Mathf.Min(t * 2f, 0.85f));

                // 激しい画面揺れ
                float shakeIntensity = 35f;
                float shake = Mathf.Sin(t * 80f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.6f, 0);

                // 赤いフラッシュ（警告）
                float flashAlpha = t < 0.2f ? Mathf.Lerp(0f, 0.9f, t / 0.2f) : Mathf.Lerp(0.9f, 0f, (t - 0.2f) / 0.8f);
                flashAlpha *= (UnityEngine.Random.value > 0.7f ? 1.5f : 1f); // ランダム強調
                flashImg.color = new Color(1f, 0.1f, 0.1f, flashAlpha);

                // 「不戦敗」テキストの爆発的拡大＋激しい点滅
                if (t < 0.5f)
                {
                    float textT = t / 0.5f;
                    float fontSize = Mathf.Lerp(1f, 180f, Mathf.Pow(textT, 0.3f));
                    forfeitTxt.fontSize = Mathf.RoundToInt(fontSize);

                    // 激しい点滅
                    bool blink = Mathf.Sin(t * 50f) > 0;
                    forfeitTxt.color = blink ? new Color(1f, 0.2f, 0.2f, 1f) : new Color(0.5f, 0f, 0f, 1f);

                    // バウンド
                    float scale = 1f + Mathf.Sin(textT * Mathf.PI * 10f) * 0.3f * (1f - textT);
                    forfeitRT.localScale = Vector3.one * scale;
                }
                else
                {
                    // 固定表示
                    forfeitTxt.fontSize = 180;
                    float pulse = 1f + Mathf.Sin(elapsed * 8f) * 0.1f;
                    forfeitRT.localScale = Vector3.one * pulse;
                }

                // 「コスト不足」テキストのフェードイン
                if (t >= 0.5f)
                {
                    float reasonT = (t - 0.5f) / 0.5f;
                    Color c = reasonTxt.color;
                    c.a = Mathf.Lerp(0f, 1f, reasonT);
                    reasonTxt.color = c;

                    float reasonScale = 1f + Mathf.Sin(reasonT * Mathf.PI) * 0.2f;
                    reasonRT.localScale = Vector3.one * reasonScale;
                }

                // バツ印パーティクル
                foreach (var (rt, velocity) in particles)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);
                        rt.Rotate(0, 0, 720f * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(1f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                // 失格文字
                foreach (var (rt, velocity, rotSpeed) in disqualifyTexts)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);
                        rt.Rotate(0, 0, rotSpeed * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(1f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            Destroy(bgPanel);
        }

        /// <summary>
        /// 超派手なゲームオーバー画面（勝敗表示＋再戦ボタン）
        /// </summary>
        System.Collections.IEnumerator ShowGameOverScreen()
        {
            // 勝敗判定
            bool isVictory = playerScore > aiScore;
            bool isDraw = playerScore == aiScore;

            // 全画面背景パネル
            var bgPanel = new GameObject("GameOverBG");
            bgPanel.transform.SetParent(canvas.transform, false);
            var bgImg = bgPanel.AddComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0);

            var bgRT = bgImg.rectTransform;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // 背景フェードイン
            float fadeInDuration = 0.5f;
            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeInDuration;
                bgImg.color = new Color(0, 0, 0, Mathf.Lerp(0f, 0.9f, t));
                yield return null;
            }
            bgImg.color = new Color(0, 0, 0, 0.9f);

            // 画面揺れ用の元位置保存
            Vector3 originalCanvasPos = canvas.transform.position;

            // 勝敗テキスト
            var resultTextGO = new GameObject("ResultText");
            resultTextGO.transform.SetParent(bgPanel.transform, false);
            var resultTxt = resultTextGO.AddComponent<Text>();
            resultTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            resultTxt.fontSize = 1; // 最初は極小
            resultTxt.fontStyle = FontStyle.Bold;
            resultTxt.alignment = TextAnchor.MiddleCenter;

            var resultRT = resultTxt.rectTransform;
            resultRT.anchorMin = new Vector2(0.5f, 0.65f);
            resultRT.anchorMax = new Vector2(0.5f, 0.65f);
            resultRT.pivot = new Vector2(0.5f, 0.5f);
            resultRT.sizeDelta = new Vector2(800, 300);

            if (isVictory)
            {
                resultTxt.text = "大勝利！";
                resultTxt.color = new Color(1f, 0.9f, 0f, 1f); // 金色
            }
            else if (isDraw)
            {
                resultTxt.text = "引き分け";
                resultTxt.color = new Color(1f, 1f, 0.3f, 1f); // 黄色
            }
            else
            {
                resultTxt.text = "敗北";
                resultTxt.color = new Color(1f, 0.2f, 0.2f, 1f); // 赤
            }

            // スコア表示
            var scoreTextGO = new GameObject("ScoreText");
            scoreTextGO.transform.SetParent(bgPanel.transform, false);
            var scoreTxt = scoreTextGO.AddComponent<Text>();
            scoreTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            scoreTxt.fontSize = 80;
            scoreTxt.fontStyle = FontStyle.Bold;
            scoreTxt.alignment = TextAnchor.MiddleCenter;
            scoreTxt.color = new Color(1f, 1f, 1f, 0f);
            scoreTxt.text = $"{playerScore}  -  {aiScore}";

            var scoreRT = scoreTxt.rectTransform;
            scoreRT.anchorMin = new Vector2(0.5f, 0.45f);
            scoreRT.anchorMax = new Vector2(0.5f, 0.45f);
            scoreRT.pivot = new Vector2(0.5f, 0.5f);
            scoreRT.sizeDelta = new Vector2(600, 150);

            // 再戦ボタン
            var retryBtnGO = new GameObject("RetryButton");
            retryBtnGO.transform.SetParent(bgPanel.transform, false);
            var retryImg = retryBtnGO.AddComponent<Image>();
            retryImg.color = new Color(0.2f, 0.6f, 1f, 0f);

            var retryRT = retryImg.rectTransform;
            retryRT.anchorMin = new Vector2(0.5f, 0.25f);
            retryRT.anchorMax = new Vector2(0.5f, 0.25f);
            retryRT.pivot = new Vector2(0.5f, 0.5f);
            retryRT.sizeDelta = new Vector2(400, 100);

            var retryBtn = retryBtnGO.AddComponent<Button>();
            retryBtn.targetGraphic = retryImg;

            var retryTextGO = new GameObject("RetryText");
            retryTextGO.transform.SetParent(retryBtnGO.transform, false);
            var retryTxt = retryTextGO.AddComponent<Text>();
            retryTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            retryTxt.fontSize = 60;
            retryTxt.fontStyle = FontStyle.Bold;
            retryTxt.alignment = TextAnchor.MiddleCenter;
            retryTxt.color = new Color(1f, 1f, 1f, 0f);
            retryTxt.text = "再戦";

            var retryTxtRT = retryTxt.rectTransform;
            retryTxtRT.anchorMin = Vector2.zero;
            retryTxtRT.anchorMax = Vector2.one;
            retryTxtRT.offsetMin = Vector2.zero;
            retryTxtRT.offsetMax = Vector2.zero;

            // ボタンクリック時
            retryBtn.onClick.AddListener(() => {
                Destroy(bgPanel);
                canvas.transform.position = originalCanvasPos;
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                );
            });

            // フラッシュエフェクト
            GameObject flash = CreateScreenFlash();
            var flashImg = flash.GetComponent<Image>();

            // パーティクル（300個）
            var particles = new List<(RectTransform rt, Vector2 velocity)>();
            for (int i = 0; i < 300; i++)
            {
                var particleGO = new GameObject($"Particle{i}");
                particleGO.transform.SetParent(bgPanel.transform, false);
                var particleTxt = particleGO.AddComponent<Text>();
                particleTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                particleTxt.fontSize = UnityEngine.Random.Range(40, 100);
                particleTxt.alignment = TextAnchor.MiddleCenter;

                if (isVictory)
                {
                    particleTxt.text = UnityEngine.Random.value > 0.5f ? "祝" : "勝";
                    particleTxt.color = new Color(1f, 0.9f, 0f, 1f);
                }
                else if (isDraw)
                {
                    particleTxt.text = "分";
                    particleTxt.color = new Color(1f, 1f, 0.5f, 1f);
                }
                else
                {
                    particleTxt.text = "負";
                    particleTxt.color = new Color(1f, 0.3f, 0.3f, 1f);
                }

                var particleRT = particleTxt.rectTransform;
                particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                particleRT.pivot = new Vector2(0.5f, 0.5f);
                particleRT.sizeDelta = new Vector2(80, 80);
                particleRT.position = new Vector3(0, 0, 0);

                float angle = UnityEngine.Random.Range(0f, 360f);
                float speed = UnityEngine.Random.Range(400f, 1000f);
                Vector2 vel = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;

                particles.Add((particleRT, vel));
            }

            // メインアニメーション（3秒）
            float animDuration = 3.0f;
            elapsed = 0f;

            while (elapsed < animDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / animDuration;

                // 勝敗テキストの爆発的拡大
                if (t < 0.6f)
                {
                    float textT = t / 0.6f;
                    float fontSize = Mathf.Lerp(1f, 200f, Mathf.Pow(textT, 0.4f));
                    resultTxt.fontSize = Mathf.RoundToInt(fontSize);

                    // バウンド
                    float scale = 1f + Mathf.Sin(textT * Mathf.PI * 5f) * 0.2f * (1f - textT);
                    resultRT.localScale = Vector3.one * scale;
                }

                // スコアのフェードイン
                if (t >= 0.6f && t < 1.2f)
                {
                    float scoreT = (t - 0.6f) / 0.6f;
                    Color c = scoreTxt.color;
                    c.a = Mathf.Lerp(0f, 1f, scoreT);
                    scoreTxt.color = c;

                    float scoreScale = 1f + Mathf.Sin(scoreT * Mathf.PI) * 0.3f;
                    scoreRT.localScale = Vector3.one * scoreScale;
                }

                // ボタンのフェードイン
                if (t >= 1.2f)
                {
                    float btnT = (t - 1.2f) / 0.8f;
                    Color imgC = retryImg.color;
                    imgC.a = Mathf.Lerp(0f, 1f, btnT);
                    retryImg.color = imgC;

                    Color txtC = retryTxt.color;
                    txtC.a = Mathf.Lerp(0f, 1f, btnT);
                    retryTxt.color = txtC;

                    // ボタンの脈動
                    float btnScale = 1f + Mathf.Sin(elapsed * 3f) * 0.1f;
                    retryRT.localScale = Vector3.one * btnScale;
                }

                // 画面揺れ
                float shakeIntensity = isVictory ? 40f : (isDraw ? 20f : 30f);
                float shake = Mathf.Sin(t * 60f) * (1f - t) * shakeIntensity;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);

                // フラッシュ
                Color flashColor;
                if (isVictory)
                    flashColor = new Color(1f, 0.9f, 0f, 1f); // 金色
                else if (isDraw)
                    flashColor = new Color(1f, 1f, 0.5f, 1f); // 黄色
                else
                    flashColor = new Color(1f, 0.2f, 0.2f, 1f); // 赤

                float flashAlpha = t < 0.3f ? Mathf.Lerp(0f, 0.8f, t / 0.3f) : Mathf.Lerp(0.8f, 0f, (t - 0.3f) / 0.7f);
                flashAlpha *= (UnityEngine.Random.value > 0.8f ? 1.5f : 1f); // ランダムに強く光る
                flashImg.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAlpha);

                // パーティクル移動
                foreach (var (rt, velocity) in particles)
                {
                    if (rt != null)
                    {
                        rt.position += (Vector3)(velocity * Time.deltaTime);
                        rt.Rotate(0, 0, 360f * Time.deltaTime);

                        var txt = rt.GetComponent<Text>();
                        if (txt != null)
                        {
                            Color c = txt.color;
                            c.a = Mathf.Lerp(1f, 0f, t);
                            txt.color = c;
                        }
                    }
                }

                yield return null;
            }

            // クリーンアップ
            canvas.transform.position = originalCanvasPos;
            Destroy(flash);
            foreach (var (rt, _) in particles)
            {
                if (rt != null) Destroy(rt.gameObject);
            }

            
            yield return new WaitForSeconds(5.0f);
            //ユーザーの最新スコアの取得
            int currentScore=PlayerPrefs.GetInt("PlayerScore");
            if(playerScore> currentScore)
            {
                PlayerPrefs.SetInt("PlayerScore", playerScore);
                PlayerPrefs.SetInt("AIScore", aiScore);
            }
            //タイトルに戻る
            UnityEngine.SceneManagement.SceneManager.LoadScene("Title");

        }

        // ========== ヘルパー関数 ==========

        /// <summary>
        /// 投射物を移動させるヘルパー
        /// </summary>
        System.Collections.IEnumerator MoveProjectile(RectTransform rt, Vector3 from, Vector3 to, float duration, bool destroyAfter)
        {
            float elapsed = 0f;
            rt.position = from;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                rt.position = Vector3.Lerp(from, to, t);
                rt.Rotate(0, 0, 500f * Time.deltaTime);

                yield return null;
            }

            if (destroyAfter) Destroy(rt.gameObject);
        }

        /// <summary>
        /// フラッシュして消えるヘルパー
        /// </summary>
        System.Collections.IEnumerator FlashAndFade(RectTransform rt, float duration)
        {
            var img = rt.GetComponent<Image>();
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // フラッシュ
                float flash = Mathf.Sin(t * Mathf.PI * 10f);
                Color c = img.color;
                c.a = Mathf.Lerp(1f, 0f, t) * Mathf.Abs(flash);
                img.color = c;

                yield return null;
            }

            Destroy(rt.gameObject);
        }

        void RenderHand()
        {
            // Clear
            for (int i = handPanel.childCount - 1; i >= 0; i--)
                Destroy(handPanel.GetChild(i).gameObject);

            // コスト不足判定はカード選択フェーズのみ
            if (currentPhase == Phase.CardSelection)
            {
                bool hasPlayableCard = false;
                for (int i = 0; i < playerHand.Count; i++)
                {
                    if (playerMana >= playerHand[i].cost)
                    {
                        hasPlayableCard = true;
                        break;
                    }
                }

                if (!hasPlayableCard && playerHand.Count > 0)
                {
                    Debug.LogWarning("コスト不足：出せるカードがありません。不戦敗処理を開始します。");
                    StartCoroutine(AutoSkipTurn());
                    return;
                }
            }

            for (int i = 0; i < playerHand.Count; i++)
            {
                var card = playerHand[i];
                var btnGO = new GameObject($"Card_{i}_{card.name}");
                btnGO.transform.SetParent(handPanel, false);

                var img = btnGO.AddComponent<Image>();
                img.sprite = LoadSprite(card.sprite);
                img.type = Image.Type.Simple;
                img.preserveAspect = true; // アスペクト比維持
                img.raycastTarget = true;
                // カードの背景色を設定（タイプ別）
                img.color = GetCardColor(card.type);

                var btn = btnGO.AddComponent<Button>();
                int idx = i;
                btn.onClick.AddListener(() => HandlePlayerChoice(idx, timedOut: false));

                // ホバーエフェクト追加
                var hoverEffect = btnGO.AddComponent<CardHoverEffect>();
                hoverEffect.normalScale = Vector3.one;
                hoverEffect.hoverScale = new Vector3(1.1f, 1.1f, 1f);

                // カード名（上部）
                var nameGO = new GameObject("CardName");
                nameGO.transform.SetParent(btnGO.transform, false);
                var nameText = nameGO.AddComponent<Text>();
                nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                nameText.alignment = TextAnchor.UpperCenter;
                nameText.fontSize = 18;
                nameText.fontStyle = FontStyle.Bold;
                nameText.color = Color.white;
                nameText.text = card.name;
                nameText.resizeTextForBestFit = false;
                // 縁取り
                var nameShadow = nameGO.AddComponent<UnityEngine.UI.Shadow>();
                nameShadow.effectColor = Color.black;
                nameShadow.effectDistance = new Vector2(2, -2);
                var nameRT = nameText.rectTransform;
                nameRT.anchorMin = new Vector2(0, 0.75f);
                nameRT.anchorMax = new Vector2(1, 1);
                nameRT.offsetMin = new Vector2(5, 5);
                nameRT.offsetMax = new Vector2(-5, -5);

                // 攻撃力（下部大きく）
                var atkGO = new GameObject("Attack");
                atkGO.transform.SetParent(btnGO.transform, false);
                var atkText = atkGO.AddComponent<Text>();
                atkText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                atkText.alignment = TextAnchor.MiddleCenter;
                atkText.fontSize = 56;
                atkText.fontStyle = FontStyle.Bold;
                atkText.color = new Color(1f, 0.9f, 0.2f); // 黄色
                atkText.text = card.attack.ToString();
                // 縁取り効果（影で代用）
                var shadow = atkGO.AddComponent<UnityEngine.UI.Shadow>();
                shadow.effectColor = Color.black;
                shadow.effectDistance = new Vector2(3, -3);
                var atkRT = atkText.rectTransform;
                atkRT.anchorMin = new Vector2(0.0f, 0.15f);
                atkRT.anchorMax = new Vector2(1.0f, 0.4f);
                atkRT.offsetMin = Vector2.zero;
                atkRT.offsetMax = Vector2.zero;

                // コスト（左下）
                var costGO = new GameObject("Cost");
                costGO.transform.SetParent(btnGO.transform, false);
                var costText = costGO.AddComponent<Text>();
                costText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                costText.alignment = TextAnchor.LowerLeft;
                costText.fontSize = 22;
                costText.fontStyle = FontStyle.Bold;
                costText.color = Color.cyan;
                costText.text = $"COST: {card.cost}";
                costText.resizeTextForBestFit = false;
                // 縁取り
                var costShadow = costGO.AddComponent<UnityEngine.UI.Shadow>();
                costShadow.effectColor = Color.black;
                costShadow.effectDistance = new Vector2(2, -2);
                var costRT = costText.rectTransform;
                costRT.anchorMin = new Vector2(0, 0);
                costRT.anchorMax = new Vector2(1, 0.15f);
                costRT.offsetMin = new Vector2(8, 5);
                costRT.offsetMax = new Vector2(-5, -5);

                // フェーズに応じた視覚効果
                bool canPlay = playerMana >= card.cost;

                if (currentPhase == Phase.CardSelection)
                {
                    // カード選択フェーズ
                    if (!canPlay)
                    {
                        // 出せないカード：暗く表示
                        img.color = new Color(img.color.r * 0.4f, img.color.g * 0.4f, img.color.b * 0.4f, 0.6f);
                        btn.interactable = false;

                        // 赤枠追加
                        var outline = btnGO.AddComponent<Outline>();
                        outline.effectColor = new Color(1f, 0.2f, 0.2f);
                        outline.effectDistance = new Vector2(3, -3);

                        // COST不足表示
                        var insuffGO = new GameObject("Insufficient");
                        insuffGO.transform.SetParent(btnGO.transform, false);
                        var insuffText = insuffGO.AddComponent<Text>();
                        insuffText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                        insuffText.alignment = TextAnchor.UpperRight;
                        insuffText.fontSize = 16;
                        insuffText.fontStyle = FontStyle.Bold;
                        insuffText.color = new Color(1f, 0.3f, 0.3f);
                        insuffText.text = "COST不足";
                        var insuffRT = insuffText.rectTransform;
                        insuffRT.anchorMin = new Vector2(0.5f, 0.7f);
                        insuffRT.anchorMax = new Vector2(1, 0.9f);
                        insuffRT.offsetMin = Vector2.zero;
                        insuffRT.offsetMax = Vector2.zero;
                    }
                    else
                    {
                        // 出せるカード：緑枠追加
                        var outline = btnGO.AddComponent<Outline>();
                        outline.effectColor = new Color(0.3f, 1f, 0.3f);
                        outline.effectDistance = new Vector2(4, -4);
                    }
                }
                else if (currentPhase == Phase.ManaGain)
                {
                    // マナ獲得フェーズ：全カード選択可能
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => HandleDiscardChoice(idx));

                    // 黄色枠追加
                    var outline = btnGO.AddComponent<Outline>();
                    outline.effectColor = new Color(1f, 1f, 0.3f);
                    outline.effectDistance = new Vector2(4, -4);

                    // +X マナ表示（右上）
                    var manaGainGO = new GameObject("ManaGain");
                    manaGainGO.transform.SetParent(btnGO.transform, false);
                    var manaGainBg = manaGainGO.AddComponent<Image>();
                    manaGainBg.color = new Color(0.6f, 0.3f, 0.8f, 0.9f); // 紫
                    var manaGainRT = manaGainBg.rectTransform;
                    manaGainRT.anchorMin = new Vector2(0.65f, 0.75f);
                    manaGainRT.anchorMax = new Vector2(0.95f, 0.95f);
                    manaGainRT.offsetMin = Vector2.zero;
                    manaGainRT.offsetMax = Vector2.zero;

                    var manaGainTextGO = new GameObject("ManaGainText");
                    manaGainTextGO.transform.SetParent(manaGainGO.transform, false);
                    var manaGainText = manaGainTextGO.AddComponent<Text>();
                    manaGainText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    manaGainText.alignment = TextAnchor.MiddleCenter;
                    manaGainText.fontSize = 20;
                    manaGainText.fontStyle = FontStyle.Bold;
                    manaGainText.color = Color.white;
                    manaGainText.text = $"+{card.attack}";
                    var manaGainTextRT = manaGainText.rectTransform;
                    manaGainTextRT.anchorMin = Vector2.zero;
                    manaGainTextRT.anchorMax = Vector2.one;
                    manaGainTextRT.offsetMin = Vector2.zero;
                    manaGainTextRT.offsetMax = Vector2.zero;
                }
            }
        }

        /// <summary>
        /// コスト不足で出せるカードがない場合の不戦敗処理
        /// </summary>
        System.Collections.IEnumerator AutoSkipTurn()
        {
            waitingForPlayer = false;

            // 不戦敗専用の超派手演出
            yield return StartCoroutine(ShowForfeitEffect());

            // AIに1ポイント付与（不戦勝）
            aiScore++;
            Debug.Log($"[コスト不足] プレイヤー不戦敗、AIに1ポイント");
            Debug.Log($"現在のスコア - プレイヤー: {playerScore}, AI: {aiScore}");

            // スコア更新
            UpdateScoreDisplay();

            // 手札から1枚ランダムに捨てる（ペナルティ）
            if (playerHand.Count > 0)
            {
                int discardIndex = UnityEngine.Random.Range(0, playerHand.Count);
                var discardedCard = playerHand[discardIndex];
                playerHand.RemoveAt(discardIndex);
                Debug.Log($"[ペナルティ] {discardedCard.name} を捨て札");
            }

            // AI側も1枚捨てる（バランス調整）
            if (aiHand.Count > 0)
            {
                int aiDiscardIndex = UnityEngine.Random.Range(0, aiHand.Count);
                var aiDiscardedCard = aiHand[aiDiscardIndex];
                aiHand.RemoveAt(aiDiscardIndex);
                Debug.Log($"[AI] {aiDiscardedCard.name} を捨て札");
            }

            // 両者2枚ドロー
            Draw(playerDeck, playerHand, 2);
            Draw(aiDeck, aiHand, 2);

            // 次のターンへ、またはゲーム終了判定
            if (playerHand.Count == 0 || aiHand.Count == 0)
            {
                Debug.Log("=== ゲーム終了 ===");
                Debug.Log($"最終スコア - プレイヤー: {playerScore}, AI: {aiScore}");
                //Debug.Log($"ログ: {logger.GetLogPath()}");

                // 超派手な最終結果画面を表示
                yield return StartCoroutine(ShowGameOverScreen());
            }
            else
            {
                yield return new UnityEngine.WaitForSeconds(0.3f);
                BeginNextTurn();
            }
        }

        void UpdateScoreDisplay()
        {
            infoText.text = $"スコア\nあなた: {playerScore}  -  AI: {aiScore}";
        }

        void HandlePlayerChoice(int index, bool timedOut)
        {
            if (!waitingForPlayer) return;
            if (index < 0 || index >= playerHand.Count) return;

            var card = playerHand[index];

            // コストチェック
            if (playerMana < card.cost)
            {
                Debug.LogWarning($"マナ不足！ 必要: {card.cost}, 現在: {playerMana}");
                return;
            }

            waitingForPlayer = false;
            float reactionMs = (Time.realtimeSinceStartup - turnStartTime) * 1000f;

            // カードを選択
            selectedCardToPlay = card;
            playerHand.RemoveAt(index);

            // マナ消費
            playerMana -= card.cost;
            UpdateManaDisplay();

            // ブースト適用
            if (playerBoostAmount > 0)
            {
                selectedCardToPlay.attack += playerBoostAmount;
                Debug.Log($"[ブースト適用] {card.name} ATK: {card.attack - playerBoostAmount} → {card.attack}");
            }

            Debug.Log($"[プレイヤー] {card.name} (COST: {card.cost}, ATK: {card.attack}) を選択、マナ消費: -{card.cost} → 残り{playerMana}");

            // バトルフェーズへ
            ProceedToBattle(selectedCardToPlay, reactionMs, timedOut);
        }

        void HandleDiscardChoice(int index)
        {
            if (!waitingForPlayer) return;
            if (index < 0 || index >= playerHand.Count) return;
            if (currentPhase != Phase.ManaGain) return;

            waitingForPlayer = false;

            var discardCard = playerHand[index];
            int gainedMana = discardCard.attack;

            Debug.Log($"[プレイヤー] {discardCard.name} (ATK: {discardCard.attack}) を捨て札、マナ獲得: +{gainedMana}");

            // AI捨て札選択（最も攻撃力の低いカードを捨てる）
            int aiGainedMana = 0;
            if (aiHand.Count > 1)
            {
                int discardIdx = 0;
                int lowestAtk = int.MaxValue;
                for (int i = 0; i < aiHand.Count; i++)
                {
                    if (aiHand[i].attack < lowestAtk)
                    {
                        lowestAtk = aiHand[i].attack;
                        discardIdx = i;
                    }
                }
                var aiDiscard = aiHand[discardIdx];
                aiGainedMana = aiDiscard.attack;
                aiHand.RemoveAt(discardIdx);
                Debug.Log($"[AI] {aiDiscard.name} (ATK: {aiDiscard.attack}) を捨て札、マナ獲得: +{aiGainedMana}");
            }
            else
            {
                Debug.Log($"[AI] 手札{aiHand.Count}枚、マナ獲得フェーズをスキップ");
            }

            // カード吸い込み演出とマナ獲得演出を実行
            StartCoroutine(ManaGainSequence(discardCard, index, gainedMana, aiGainedMana));
        }

        System.Collections.IEnumerator ManaGainSequence(Card discardCard, int cardIndex, int playerGain, int aiGain)
        {
            // 手札から選択したカードのGameObjectを見つける
            Transform selectedCardTransform = null;
            if (cardIndex < handPanel.childCount)
            {
                selectedCardTransform = handPanel.GetChild(cardIndex);
            }

            if (selectedCardTransform != null)
            {
                // カード吸い込み演出
                var cardImage = selectedCardTransform.GetComponent<Image>();
                var cardRT = selectedCardTransform.GetComponent<RectTransform>();

                // カード全体の透明度を制御するためにCanvasGroupを追加
                var canvasGroup = selectedCardTransform.gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = selectedCardTransform.gameObject.AddComponent<CanvasGroup>();
                }

                // 渦巻きエフェクトを作成
                var vortexGO = new GameObject("VortexEffect");
                vortexGO.transform.SetParent(canvas.transform, false);
                var vortexRT = vortexGO.AddComponent<RectTransform>();
                vortexRT.anchorMin = new Vector2(0.5f, 0.5f);
                vortexRT.anchorMax = new Vector2(0.5f, 0.5f);
                vortexRT.pivot = new Vector2(0.5f, 0.5f);
                vortexRT.sizeDelta = new Vector2(400, 400);
                vortexRT.position = manaText.rectTransform.position;

                // パーティクル風の光の粒+魔法文字を作成（螺旋状に配置）
                var particles = new List<(RectTransform rt, bool isText, float angle, float radius, float speed)>();
                int particleCount = 40; // 30→40に増加
                // ラテン語風の魔法単語と記号（フォント対応文字のみ）
                string[] magicWords = { "LUX", "VIS", "ARS", "SOL", "NOX", "REX", "LEX", "PAX", "VOX", "LUC" }; // ラテン語風
                string[] magicSymbols = { "※", "★", "☆", "◆", "◇", "●", "○", "■", "□", "▲", "△", "∞", "+", "×" }; // 記号

                for (int i = 0; i < particleCount; i++)
                {
                    var particleGO = new GameObject($"Particle{i}");
                    particleGO.transform.SetParent(vortexGO.transform, false);

                    bool isText = (i % 3 == 0); // 3つに1つは文字
                    RectTransform particleRT;

                    if (isText)
                    {
                        // 魔法文字
                        var text = particleGO.AddComponent<Text>();
                        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                        text.fontSize = 32; // より大きく
                        text.fontStyle = FontStyle.Bold;
                        text.alignment = TextAnchor.MiddleCenter;
                        text.color = new Color(1f, 0.95f, 0.3f, 1f); // より明るい金色

                        // ランダムに単語or記号を選択
                        if (i % 6 < 3)
                            text.text = magicWords[UnityEngine.Random.Range(0, magicWords.Length)];
                        else
                            text.text = magicSymbols[UnityEngine.Random.Range(0, magicSymbols.Length)];

                        // Outlineでくっきりさせる
                        var outline = particleGO.AddComponent<Outline>();
                        outline.effectColor = new Color(0.5f, 0.25f, 0f, 1f); // 濃い茶色の縁取り
                        outline.effectDistance = new Vector2(3, -3); // より太く

                        // 影でさらに光らせる
                        var shadow = particleGO.AddComponent<UnityEngine.UI.Shadow>();
                        shadow.effectColor = new Color(1f, 0.85f, 0.2f, 0.8f); // 黄金の光
                        shadow.effectDistance = new Vector2(1, -1);

                        particleRT = text.rectTransform;
                        particleRT.sizeDelta = new Vector2(60, 50); // さらに大きく
                    }
                    else
                    {
                        // 光の粒
                        var particleImg = particleGO.AddComponent<Image>();
                        particleImg.color = new Color(0.7f, 0.4f, 0.95f, 1f); // より濃い紫
                        particleRT = particleImg.rectTransform;
                        particleRT.sizeDelta = new Vector2(12, 12);
                    }

                    particleRT.anchorMin = new Vector2(0.5f, 0.5f);
                    particleRT.anchorMax = new Vector2(0.5f, 0.5f);
                    particleRT.pivot = new Vector2(0.5f, 0.5f);

                    // 初期位置（螺旋状に配置）
                    float angle = (360f / particleCount) * i + UnityEngine.Random.Range(-10f, 10f);
                    float radius = 180f + (i % 6) * 15f; // 外側から
                    float speed = 0.8f + (i % 4) * 0.2f; // 速度のばらつき

                    particles.Add((particleRT, isText, angle, radius, speed));
                }

                // フィボナッチ螺旋を使った自然な渦（複数の螺旋線）
                var spiralLines = new List<(List<Image> points, float rotationSpeed)>();
                int spiralCount = 6; // 螺旋の本数
                float goldenAngle = 137.5f; // 黄金角

                for (int s = 0; s < spiralCount; s++)
                {
                    var linePoints = new List<Image>();
                    int pointCount = 25; // 各螺旋の点の数
                    float angleOffset = (360f / spiralCount) * s; // 各螺旋の開始角度をずらす

                    for (int i = 0; i < pointCount; i++)
                    {
                        var pointGO = new GameObject($"SpiralPoint_S{s}_P{i}");
                        pointGO.transform.SetParent(vortexGO.transform, false);
                        var pointImg = pointGO.AddComponent<Image>();

                        // 外側から内側へ向かって色を変化（紫→青紫）
                        float t = (float)i / pointCount;
                        float hue = Mathf.Lerp(0.75f, 0.85f, t); // 紫系
                        Color spiralColor = Color.HSVToRGB(hue, 0.7f, 0.9f);
                        spiralColor.a = Mathf.Lerp(0.6f, 0.2f, t); // 内側ほど薄く
                        pointImg.color = spiralColor;

                        var pointRT = pointImg.rectTransform;
                        pointRT.anchorMin = new Vector2(0.5f, 0.5f);
                        pointRT.anchorMax = new Vector2(0.5f, 0.5f);
                        pointRT.pivot = new Vector2(0.5f, 0.5f);

                        // フィボナッチ螺旋の計算（極座標）
                        float angle = angleOffset + i * goldenAngle;
                        float radius = Mathf.Sqrt(i + 1) * 15f; // √n で半径を決定（自然な広がり）

                        float x = Mathf.Cos(angle * Mathf.Deg2Rad) * radius;
                        float y = Mathf.Sin(angle * Mathf.Deg2Rad) * radius;
                        pointRT.anchoredPosition = new Vector2(x, y);

                        // サイズは外側ほど大きく
                        float size = Mathf.Lerp(8f, 3f, t);
                        pointRT.sizeDelta = new Vector2(size, size);

                        linePoints.Add(pointImg);
                    }

                    // 各螺旋の回転速度を少しずつ変える
                    float rotSpeed = 120f + s * 30f;
                    spiralLines.Add((linePoints, rotSpeed));
                }

                // 元の位置とマナ表示の位置を取得
                Vector3 startPos = cardRT.position;
                Vector3 targetPos = manaText.rectTransform.position;

                float duration = 1.5f; // 0.8秒 → 1.5秒に延長
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / duration;
                    float easedT = Mathf.Pow(t, 2f); // ease-in (加速)

                    // パーティクルを螺旋状に回転・吸い込み
                    for (int i = 0; i < particles.Count; i++)
                    {
                        var particle = particles[i];

                        // 角度を更新（回転速度）
                        float currentAngle = particle.angle + elapsed * 600f * particle.speed; // 少しゆっくりに

                        // 半径を縮小（中心に吸い込まれる）
                        float currentRadius = particle.radius * (1f - easedT);

                        // 位置計算
                        float x = Mathf.Cos(currentAngle * Mathf.Deg2Rad) * currentRadius;
                        float y = Mathf.Sin(currentAngle * Mathf.Deg2Rad) * currentRadius;
                        particle.rt.anchoredPosition = new Vector2(x, y);

                        // フェード（中心に近づくほど薄く、でも濃いめに）
                        float distanceAlpha = currentRadius / particle.radius;
                        float baseAlpha = Mathf.Sin(t * Mathf.PI) * distanceAlpha;

                        if (particle.isText)
                        {
                            // 文字は金色でより目立つ
                            var textComp = particle.rt.GetComponent<Text>();
                            Color c = textComp.color;
                            c.a = Mathf.Clamp01(baseAlpha * 1.5f); // より濃く、上限1.0
                            textComp.color = c;

                            // Outlineとshadowの透明度も連動
                            var outline = particle.rt.GetComponent<Outline>();
                            if (outline != null)
                            {
                                Color oc = outline.effectColor;
                                oc.a = c.a;
                                outline.effectColor = oc;
                            }
                            var shadow = particle.rt.GetComponent<UnityEngine.UI.Shadow>();
                            if (shadow != null)
                            {
                                Color sc = shadow.effectColor;
                                sc.a = c.a * 0.8f;
                                shadow.effectColor = sc;
                            }

                            // 文字は回転させて魔法陣っぽく
                            particle.rt.rotation = Quaternion.Euler(0, 0, currentAngle * 0.5f);
                        }
                        else
                        {
                            // 光の粒
                            var imgComp = particle.rt.GetComponent<Image>();
                            Color c = imgComp.color;
                            c.a = baseAlpha * 1.0f;
                            imgComp.color = c;
                        }

                        // サイズも徐々に縮小
                        float particleScale = Mathf.Lerp(1f, 0.2f, 1f - distanceAlpha);
                        particle.rt.localScale = Vector3.one * particleScale;
                    }

                    // フィボナッチ螺旋を回転・脈動させる
                    for (int s = 0; s < spiralLines.Count; s++)
                    {
                        var spiral = spiralLines[s];
                        float rotation = spiral.rotationSpeed * elapsed;

                        // 各螺旋を回転
                        for (int i = 0; i < spiral.points.Count; i++)
                        {
                            var point = spiral.points[i];
                            float pointT = (float)i / spiral.points.Count;

                            // 透明度アニメーション（波打つ）
                            float wave = Mathf.Sin(t * Mathf.PI * 2f + pointT * Mathf.PI * 2f);
                            float baseAlpha = Mathf.Lerp(0.6f, 0.2f, pointT);
                            Color c = point.color;
                            c.a = baseAlpha * Mathf.Sin(t * Mathf.PI) * (0.8f + wave * 0.2f);
                            point.color = c;

                            // サイズの脈動
                            float pulse = 1f + Mathf.Sin(elapsed * 3f + pointT * Mathf.PI) * 0.2f;
                            float baseSize = Mathf.Lerp(8f, 3f, pointT);
                            point.rectTransform.sizeDelta = new Vector2(baseSize * pulse, baseSize * pulse);

                            // 回転による位置更新
                            float originalAngle = (360f / spiralLines.Count) * s + i * 137.5f;
                            float currentAngle = originalAngle + rotation;
                            float radius = Mathf.Sqrt(i + 1) * 15f;

                            float x = Mathf.Cos(currentAngle * Mathf.Deg2Rad) * radius;
                            float y = Mathf.Sin(currentAngle * Mathf.Deg2Rad) * radius;
                            point.rectTransform.anchoredPosition = new Vector2(x, y);
                        }
                    }

                    // カードが渦に吸い込まれる軌道（螺旋）
                    float cardAngle = t * 720f; // 2回転
                    float cardRadius = Mathf.Lerp(200f, 0f, easedT); // 半径が徐々に小さく
                    float spiralX = Mathf.Cos(cardAngle * Mathf.Deg2Rad) * cardRadius;
                    float spiralY = Mathf.Sin(cardAngle * Mathf.Deg2Rad) * cardRadius;

                    Vector3 spiralOffset = new Vector3(spiralX, spiralY, 0);
                    cardRT.position = Vector3.Lerp(startPos, targetPos, easedT) + spiralOffset;

                    // カード回転（渦の流れに合わせて）
                    cardRT.rotation = Quaternion.Euler(0, 0, -cardAngle);

                    // 縮小
                    float scale = Mathf.Lerp(1f, 0f, easedT);
                    cardRT.localScale = Vector3.one * scale;

                    // カード全体をフェードアウト（子要素含む）
                    canvasGroup.alpha = Mathf.Lerp(1f, 0f, easedT);

                    yield return null;
                }

                // 渦巻きエフェクトを削除
                Destroy(vortexGO);
            }

            // カードを手札から削除
            playerHand.RemoveAt(cardIndex);

            // マナ獲得量を大きく表示
            yield return StartCoroutine(ShowManaGainText(playerGain));

            // マナ加算
            int oldMana = playerMana;
            playerMana += playerGain;
            aiMana += aiGain;

            // マナ数値カウントアップアニメーション
            yield return StartCoroutine(AnimateManaCountUp(oldMana, playerMana));

            Debug.Log($"プレイヤーマナ: {playerMana}, AIマナ: {aiMana}");

            // カード選択フェーズへ
            StartCardSelectionPhase();
        }

        System.Collections.IEnumerator ShowManaGainText(int gainAmount)
        {
            // +X マナ テキストを作成
            var gainTextGO = new GameObject("ManaGainText");
            gainTextGO.transform.SetParent(canvas.transform, false);
            var gainText = gainTextGO.AddComponent<Text>();
            gainText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            gainText.fontSize = 100; // 少し小さく
            gainText.fontStyle = FontStyle.Bold;
            gainText.alignment = TextAnchor.MiddleCenter;
            gainText.color = new Color(0.3f, 1f, 0.3f); // 明るい緑
            gainText.text = $"+{gainAmount}";
            gainText.supportRichText = true;

            // 影追加
            var shadow = gainTextGO.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(5, -5);

            var gainRT = gainText.rectTransform;
            // Anchorとpivotを中央に設定
            gainRT.anchorMin = new Vector2(0.5f, 0.5f);
            gainRT.anchorMax = new Vector2(0.5f, 0.5f);
            gainRT.pivot = new Vector2(0.5f, 0.5f);
            gainRT.sizeDelta = new Vector2(400, 200);

            // マナ表示の少し下（画面内に収まる位置）
            gainRT.position = manaText.rectTransform.position + new Vector3(0, -100, 0);

            // アニメーション: 浮かび上がる → 拡大 → マナ表示に吸い込まれる
            float duration = 1.0f;
            float elapsed = 0f;
            Vector3 startPos = gainRT.position;
            Vector3 targetPos = manaText.rectTransform.position;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                if (t < 0.3f)
                {
                    // 浮かび上がる + 拡大
                    float t1 = t / 0.3f;
                    float scale = Mathf.Lerp(0.8f, 1.6f, t1); // 少し控えめに
                    gainRT.localScale = Vector3.one * scale;
                    gainRT.position = startPos + Vector3.up * (30f * t1); // 浮かび上がり距離を調整
                }
                else
                {
                    // 吸い込まれる
                    float t2 = (t - 0.3f) / 0.7f;
                    float easedT = Mathf.Pow(t2, 2f); // ease-in
                    gainRT.position = Vector3.Lerp(startPos + Vector3.up * 30f, targetPos, easedT);
                    gainRT.localScale = Vector3.one * Mathf.Lerp(1.6f, 0.3f, easedT);

                    // フェードアウト
                    Color c = gainText.color;
                    c.a = Mathf.Lerp(1f, 0f, easedT);
                    gainText.color = c;
                }

                yield return null;
            }

            Destroy(gainTextGO);
        }

        System.Collections.IEnumerator AnimateManaCountUp(int startValue, int endValue)
        {
            float duration = 0.5f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // カウントアップ
                int currentValue = Mathf.RoundToInt(Mathf.Lerp(startValue, endValue, t));
                manaText.text = $"マナ: <color=yellow>{currentValue}</color>";

                // パルスエフェクト
                float pulse = 1f + Mathf.Sin(t * Mathf.PI * 4f) * 0.15f;
                manaText.transform.localScale = Vector3.one * pulse;

                yield return null;
            }

            // 最終値を設定
            manaText.text = $"マナ: <color=yellow>{endValue}</color>";
            manaText.transform.localScale = Vector3.one;
        }

        void ProceedToBattle(Card playerCard, float reactionMs, bool timedOut)
        {
            currentPhase = Phase.Battle;
            UpdatePhaseDisplay();

            // AI行動
            // AI: コストを支払えるカードの中から最も攻撃力の高いカードを選択
            int aiIndex = -1;
            int bestAttack = int.MinValue;
            for (int i = 0; i < aiHand.Count; i++)
            {
                if (aiMana >= aiHand[i].cost && aiHand[i].attack > bestAttack)
                {
                    bestAttack = aiHand[i].attack;
                    aiIndex = i;
                }
            }

            // 出せるカードがない場合は最もコストの低いカードを探す（強制的に出す）
            if (aiIndex == -1)
            {
                int lowestCost = int.MaxValue;
                for (int i = 0; i < aiHand.Count; i++)
                {
                    if (aiHand[i].cost < lowestCost)
                    {
                        lowestCost = aiHand[i].cost;
                        aiIndex = i;
                    }
                }
            }

            var aiCard = aiHand[aiIndex];
            aiHand.RemoveAt(aiIndex);

            // AIマナ消費（マイナスになる可能性もある）
            aiMana -= aiCard.cost;
            Debug.Log($"[AI] {aiCard.name} (COST: {aiCard.cost}, ATK: {aiCard.attack}) を選択、マナ消費: -{aiCard.cost} → 残り{aiMana}");

            // 演出を開始
            StartCoroutine(BattleSequence(playerCard, aiCard, reactionMs, timedOut));
        }

        void BeginNextTurn()
        {
            selectedCardToPlay = null;
            playerBoostAmount = 0; // ブーストリセット
            ResetCenterCards();

            Debug.Log($"\n--- ターン {turnNumber + 1} 開始 ---");
            Debug.Log($"残り手札: {playerHand.Count}枚, プレイヤーマナ: {playerMana}, AIマナ: {aiMana}");

            // 手札が2枚以上ならマナ獲得フェーズから開始
            if (playerHand.Count > 1)
            {
                currentPhase = Phase.ManaGain;
                UpdatePhaseDisplay();
                RenderHand();
                waitingForPlayer = true;
                turnStartTime = Time.realtimeSinceStartup;
            }
            else
            {
                // 手札1枚以下ならマナ獲得スキップしてカード選択へ
                Debug.Log($"[プレイヤー] 手札{playerHand.Count}枚、マナ獲得フェーズをスキップ");
                StartCardSelectionPhase();
            }
        }

        void StartCardSelectionPhase()
        {
            currentPhase = Phase.CardSelection;
            UpdatePhaseDisplay();

            // マナアクション選択を表示
            if (playerMana >= 2) // マナが2以上なら選択肢を表示
            {
                ShowManaActionChoice();
            }
            else
            {
                // マナ不足なら直接カード選択へ
                RenderHand();
                waitingForPlayer = true;
                turnStartTime = Time.realtimeSinceStartup;
            }
        }

        void ShowManaActionChoice()
        {
            waitingForManaAction = true;
            manaActionPanel.SetActive(true);

            // ボタンの有効/無効を設定
            UpdateManaActionButtons();

            // フェードイン演出
            StartCoroutine(FadeInManaActionPanel());
        }

        IEnumerator FadeInManaActionPanel()
        {
            var canvasGroup = manaActionPanel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = manaActionPanel.AddComponent<CanvasGroup>();

            canvasGroup.alpha = 0f;
            float elapsed = 0f;
            float duration = 0.25f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / duration);
                yield return null;
            }

            canvasGroup.alpha = 1f;
        }

        void UpdateManaActionButtons()
        {
            // ブースト: マナ2以上で有効
            boostButton.interactable = playerMana >= 2;
            var boostText = boostButton.GetComponentInChildren<Text>();
            boostText.text = $"ブースト\n(マナ2/4/6)";

            // マリガン: マナ3以上で有効
            mulliganButton.interactable = playerMana >= 3 && playerHand.Count > 0;
            var mulliganText = mulliganButton.GetComponentInChildren<Text>();
            mulliganText.text = $"マリガン\n(マナ3/5)";

            // スキップ: 常に有効
            skipButton.interactable = true;
        }

        void HandleManaAction_Boost()
        {
            // ブースト量選択UIを表示
            manaActionPanel.SetActive(false);
            ShowBoostAmountChoice();
        }

        void ShowBoostAmountChoice()
        {
            // ブースト量選択パネル作成
            var boostChoicePanel = new GameObject("BoostChoicePanel");
            boostChoicePanel.transform.SetParent(canvas.transform, false);
            var boostChoiceRT = boostChoicePanel.AddComponent<RectTransform>();
            boostChoiceRT.anchorMin = new Vector2(0.25f, 0.25f);
            boostChoiceRT.anchorMax = new Vector2(0.75f, 0.75f);
            boostChoiceRT.offsetMin = Vector2.zero;
            boostChoiceRT.offsetMax = Vector2.zero;

            var bgImg = boostChoicePanel.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);

            // タイトル
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(boostChoicePanel.transform, false);
            var titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 0.8f);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;

            var titleText = titleGO.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 32;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.text = "ブースト量を選択";

            // ボタンエリア
            var buttonAreaGO = new GameObject("ButtonArea");
            buttonAreaGO.transform.SetParent(boostChoicePanel.transform, false);
            var buttonAreaRT = buttonAreaGO.AddComponent<RectTransform>();
            buttonAreaRT.anchorMin = new Vector2(0.1f, 0.1f);
            buttonAreaRT.anchorMax = new Vector2(0.9f, 0.75f);
            buttonAreaRT.offsetMin = Vector2.zero;
            buttonAreaRT.offsetMax = Vector2.zero;

            // ブーストボタン3つ（+1, +2, +3）とキャンセル
            float buttonHeight = 0.22f;
            float spacing = 0.04f;
            float startY = 1f - buttonHeight;

            // ATK+1 (マナ2)
            if (playerMana >= 2)
            {
                CreateBoostButton(buttonAreaGO.transform, "ATK+1 (マナ2消費)",
                    new Vector2(0, startY - (buttonHeight + spacing) * 0),
                    new Vector2(1, startY - (buttonHeight + spacing) * 0 + buttonHeight),
                    () => ApplyBoost(1, 2, boostChoicePanel));
                startY -= (buttonHeight + spacing);
            }

            // ATK+2 (マナ4)
            if (playerMana >= 4)
            {
                CreateBoostButton(buttonAreaGO.transform, "ATK+2 (マナ4消費)",
                    new Vector2(0, startY - (buttonHeight + spacing) * 0),
                    new Vector2(1, startY - (buttonHeight + spacing) * 0 + buttonHeight),
                    () => ApplyBoost(2, 4, boostChoicePanel));
                startY -= (buttonHeight + spacing);
            }

            // ATK+3 (マナ6)
            if (playerMana >= 6)
            {
                CreateBoostButton(buttonAreaGO.transform, "ATK+3 (マナ6消費)",
                    new Vector2(0, startY - (buttonHeight + spacing) * 0),
                    new Vector2(1, startY - (buttonHeight + spacing) * 0 + buttonHeight),
                    () => ApplyBoost(3, 6, boostChoicePanel));
                startY -= (buttonHeight + spacing);
            }

            // キャンセルボタン
            CreateBoostButton(buttonAreaGO.transform, "キャンセル",
                new Vector2(0, 0),
                new Vector2(1, buttonHeight),
                () => CancelBoostChoice(boostChoicePanel));
        }

        void CreateBoostButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction action)
        {
            var btnGO = new GameObject($"Btn_{label}");
            btnGO.transform.SetParent(parent, false);
            var btnRT = btnGO.AddComponent<RectTransform>();
            btnRT.anchorMin = anchorMin;
            btnRT.anchorMax = anchorMax;
            btnRT.offsetMin = Vector2.zero;
            btnRT.offsetMax = Vector2.zero;

            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = new Color(0.3f, 0.5f, 0.7f);

            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.onClick.AddListener(action);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var txt = textGO.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 28;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = label;
        }

        void ApplyBoost(int boostAmount, int manaCost, GameObject boostChoicePanel)
        {
            playerBoostAmount = boostAmount;
            playerMana -= manaCost;
            Debug.Log($"[ブースト] マナ{manaCost}消費、ATK+{boostAmount}");

            UpdateManaDisplay();
            Destroy(boostChoicePanel);

            // パワーアップ演出
            StartCoroutine(BoostEffectSequence());
        }

        void CancelBoostChoice(GameObject boostChoicePanel)
        {
            Destroy(boostChoicePanel);
            manaActionPanel.SetActive(true);
        }

        IEnumerator BoostEffectSequence()
        {
            // 画面フラッシュ（金色）
            GameObject boostFlash = CreateScreenFlash();
            var flashImg = boostFlash.GetComponent<Image>();

            // 画面中央に「パワーアップ!」を表示
            var boostEffectGO = new GameObject("BoostEffect");
            boostEffectGO.transform.SetParent(canvas.transform, false);
            var boostEffectRT = boostEffectGO.AddComponent<RectTransform>();
            boostEffectRT.anchorMin = new Vector2(0.5f, 0.5f);
            boostEffectRT.anchorMax = new Vector2(0.5f, 0.5f);
            boostEffectRT.anchoredPosition = Vector2.zero;
            boostEffectRT.sizeDelta = new Vector2(1000, 300);

            // 半透明背景
            var bgImg = boostEffectGO.AddComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0.8f);

            // テキスト
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(boostEffectGO.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var txt = textGO.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 120;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(1f, 0.8f, 0f); // 金色
            txt.text = $"パワーアップ！\nATK+{playerBoostAmount}";

            // 影（より濃く）
            var shadow = textGO.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(6, -6);

            // 光のパーティクル
            for (int i = 0; i < 30; i++)
            {
                CreateBoostParticle(boostEffectGO.transform);
            }

            // 画面揺れ + 拡大演出 + フラッシュ
            Vector3 originalCanvasPos = canvas.transform.position;
            float elapsed = 0f;
            float duration = 0.5f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float scale = Mathf.Lerp(0f, 2.0f, t);
                boostEffectRT.localScale = Vector3.one * scale;

                // 画面揺れ
                float shake = Mathf.Sin(t * 30f) * (1f - t) * 8f;
                canvas.transform.position = originalCanvasPos + new Vector3(shake, shake * 0.5f, 0);

                // フラッシュ（金色）
                float alpha = t < 0.3f ? Mathf.Lerp(0f, 0.5f, t / 0.3f) : Mathf.Lerp(0.5f, 0f, (t - 0.3f) / 0.7f);
                flashImg.color = new Color(1f, 0.8f, 0f, alpha);

                yield return null;
            }

            canvas.transform.position = originalCanvasPos;
            Destroy(boostFlash);

            yield return new WaitForSeconds(1.2f);

            // フェードアウト
            elapsed = 0f;
            duration = 0.4f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
                bgImg.color = new Color(0, 0, 0, alpha * 0.8f);
                txt.color = new Color(1f, 0.8f, 0f, alpha);
                yield return null;
            }

            Destroy(boostEffectGO);
            CloseManaActionPanel();
        }

        /// <summary>
        /// ブースト時の光パーティクル
        /// </summary>
        void CreateBoostParticle(Transform parent)
        {
            var particleGO = new GameObject("BoostParticle");
            particleGO.transform.SetParent(parent, false);
            var particleRT = particleGO.AddComponent<RectTransform>();
            particleRT.anchorMin = new Vector2(0.5f, 0.5f);
            particleRT.anchorMax = new Vector2(0.5f, 0.5f);
            particleRT.anchoredPosition = Vector2.zero;
            particleRT.sizeDelta = new Vector2(20, 20);

            var particleImg = particleGO.AddComponent<Image>();
            particleImg.color = new Color(1f, 1f, 0.5f, 1f);
            particleImg.raycastTarget = false;

            var animator = particleGO.AddComponent<BoostParticleAnimator>();
            float angle = UnityEngine.Random.Range(0f, 360f);
            float speed = UnityEngine.Random.Range(100f, 300f);
            animator.velocity = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
        }

        /// <summary>
        /// ブーストパーティクルアニメーション
        /// </summary>
        public class BoostParticleAnimator : MonoBehaviour
        {
            public Vector2 velocity;
            private RectTransform rt;
            private Image img;
            private float lifetime = 0f;

            void Start()
            {
                rt = GetComponent<RectTransform>();
                img = GetComponent<Image>();
            }

            void Update()
            {
                lifetime += Time.deltaTime;
                rt.anchoredPosition += velocity * Time.deltaTime;

                float alpha = Mathf.Lerp(1f, 0f, lifetime / 1.5f);
                img.color = new Color(1f, 1f, 0.5f, alpha);

                if (lifetime > 1.5f)
                {
                    Destroy(gameObject);
                }
            }
        }

        void HandleManaAction_Mulligan()
        {
            int exchangeCount = 0;

            // マリガン実行（簡易版：手札1枚交換）
            if (playerMana >= 5 && playerHand.Count >= 2)
            {
                // 2枚交換
                var card1 = playerHand[0];
                var card2 = playerHand[1];
                playerHand.RemoveAt(0);
                playerHand.RemoveAt(0);
                playerDeck.Add(card1);
                playerDeck.Add(card2);
                Shuffle(playerDeck);
                Draw(playerDeck, playerHand, 2);
                playerMana -= 5;
                exchangeCount = 2;
                Debug.Log("[マリガン] マナ5消費、手札2枚交換");
            }
            else if (playerMana >= 3 && playerHand.Count >= 1)
            {
                // 1枚交換
                var card = playerHand[0];
                playerHand.RemoveAt(0);
                playerDeck.Add(card);
                Shuffle(playerDeck);
                Draw(playerDeck, playerHand, 1);
                playerMana -= 3;
                exchangeCount = 1;
                Debug.Log("[マリガン] マナ3消費、手札1枚交換");
            }

            UpdateManaDisplay();

            // マリガン演出
            if (exchangeCount > 0)
            {
                StartCoroutine(MulliganEffectSequence(exchangeCount));
            }
            else
            {
                CloseManaActionPanel();
            }
        }

        IEnumerator MulliganEffectSequence(int exchangeCount)
        {
            // フラッシュ（水色）
            GameObject mulliganFlash = CreateScreenFlash();
            var flashImg = mulliganFlash.GetComponent<Image>();

            // 画面中央に「カード交換!」を表示
            var mulliganEffectGO = new GameObject("MulliganEffect");
            mulliganEffectGO.transform.SetParent(canvas.transform, false);
            var mulliganEffectRT = mulliganEffectGO.AddComponent<RectTransform>();
            mulliganEffectRT.anchorMin = new Vector2(0.5f, 0.5f);
            mulliganEffectRT.anchorMax = new Vector2(0.5f, 0.5f);
            mulliganEffectRT.anchoredPosition = Vector2.zero;
            mulliganEffectRT.sizeDelta = new Vector2(1000, 300);

            // 半透明背景
            var bgImg = mulliganEffectGO.AddComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0.8f);

            // テキスト
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(mulliganEffectGO.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var txt = textGO.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 120;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(0.4f, 0.8f, 1f); // 水色
            txt.text = $"カード交換！\n{exchangeCount}枚";

            // 影
            var shadow = textGO.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(6, -6);

            // カード風のパーティクル
            for (int i = 0; i < 15; i++)
            {
                CreateMulliganParticle();
            }

            // 横から高速スライドイン + 回転 + フラッシュ
            mulliganEffectRT.anchoredPosition = new Vector2(-2000f, 0f);
            float elapsed = 0f;
            float duration = 0.4f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                mulliganEffectRT.anchoredPosition = Vector2.Lerp(new Vector2(-2000f, 0f), Vector2.zero, t);

                // 回転しながら登場
                float rotation = Mathf.Lerp(-180f, 0f, t);
                mulliganEffectRT.rotation = Quaternion.Euler(0, 0, rotation);

                // フラッシュ（水色）
                float alpha = t < 0.3f ? Mathf.Lerp(0f, 0.5f, t / 0.3f) : Mathf.Lerp(0.5f, 0f, (t - 0.3f) / 0.7f);
                flashImg.color = new Color(0.4f, 0.8f, 1f, alpha);

                yield return null;
            }

            Destroy(mulliganFlash);

            yield return new WaitForSeconds(1.2f);

            // 横へ高速スライドアウト + 回転
            elapsed = 0f;
            duration = 0.4f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                mulliganEffectRT.anchoredPosition = Vector2.Lerp(Vector2.zero, new Vector2(2000f, 0f), t);

                // 回転しながら退場
                float rotation = Mathf.Lerp(0f, 180f, t);
                mulliganEffectRT.rotation = Quaternion.Euler(0, 0, rotation);

                float alpha = Mathf.Lerp(1f, 0f, t);
                bgImg.color = new Color(0, 0, 0, alpha * 0.8f);
                txt.color = new Color(0.4f, 0.8f, 1f, alpha);
                yield return null;
            }

            Destroy(mulliganEffectGO);
            CloseManaActionPanel();
        }

        /// <summary>
        /// マリガン時のカード風パーティクル
        /// </summary>
        void CreateMulliganParticle()
        {
            var particleGO = new GameObject("MulliganParticle");
            particleGO.transform.SetParent(canvas.transform, false);
            var particleRT = particleGO.AddComponent<RectTransform>();
            particleRT.anchorMin = new Vector2(0.5f, 0.5f);
            particleRT.anchorMax = new Vector2(0.5f, 0.5f);
            particleRT.anchoredPosition = new Vector2(UnityEngine.Random.Range(-300f, 300f), UnityEngine.Random.Range(-200f, 200f));
            particleRT.sizeDelta = new Vector2(50, 70);

            var particleImg = particleGO.AddComponent<Image>();
            particleImg.color = new Color(0.6f, 0.9f, 1f, 1f);
            particleImg.raycastTarget = false;

            var animator = particleGO.AddComponent<MulliganParticleAnimator>();
            animator.rotationSpeed = UnityEngine.Random.Range(-500f, 500f);
        }

        /// <summary>
        /// マリガンパーティクルアニメーション
        /// </summary>
        public class MulliganParticleAnimator : MonoBehaviour
        {
            public float rotationSpeed;
            private RectTransform rt;
            private Image img;
            private float lifetime = 0f;

            void Start()
            {
                rt = GetComponent<RectTransform>();
                img = GetComponent<Image>();
            }

            void Update()
            {
                lifetime += Time.deltaTime;
                rt.Rotate(0, 0, rotationSpeed * Time.deltaTime);

                float alpha = Mathf.Lerp(1f, 0f, lifetime / 1.5f);
                img.color = new Color(0.6f, 0.9f, 1f, alpha);

                if (lifetime > 1.5f)
                {
                    Destroy(gameObject);
                }
            }
        }

        void HandleManaAction_Skip()
        {
            Debug.Log("[スキップ] マナアクションなし");
            CloseManaActionPanel();
        }

        /// <summary>
        /// 画面全体フラッシュエフェクト（最高レア用）
        /// </summary>
        GameObject CreateScreenFlash()
        {
            var flashGO = new GameObject("ScreenFlash");
            flashGO.transform.SetParent(canvas.transform, false);
            var flashRT = flashGO.AddComponent<RectTransform>();
            flashRT.anchorMin = Vector2.zero;
            flashRT.anchorMax = Vector2.one;
            flashRT.offsetMin = Vector2.zero;
            flashRT.offsetMax = Vector2.zero;

            var flashImg = flashGO.AddComponent<Image>();
            flashImg.color = new Color(1, 1, 1, 0);
            flashImg.raycastTarget = false;

            return flashGO;
        }

        /// <summary>
        /// カード周囲にオーラエフェクト（最高レア用）
        /// </summary>
        GameObject CreateAuraEffect(RectTransform cardTransform, Color auraColor)
        {
            var auraGO = new GameObject("Aura");
            auraGO.transform.SetParent(cardTransform, false);
            var auraRT = auraGO.AddComponent<RectTransform>();
            auraRT.anchorMin = new Vector2(0.5f, 0.5f);
            auraRT.anchorMax = new Vector2(0.5f, 0.5f);
            auraRT.anchoredPosition = Vector2.zero;
            auraRT.sizeDelta = new Vector2(400, 550);

            var auraImg = auraGO.AddComponent<Image>();
            auraImg.color = new Color(auraColor.r, auraColor.g, auraColor.b, 0.5f);
            auraImg.raycastTarget = false;

            // パルスアニメーション
            var pulser = auraGO.AddComponent<AuraPulser>();
            pulser.baseColor = auraColor;

            return auraGO;
        }

        /// <summary>
        /// オーラのパルスアニメーション用コンポーネント
        /// </summary>
        public class AuraPulser : MonoBehaviour
        {
            public Color baseColor;
            private Image img;
            private float time;

            void Start()
            {
                img = GetComponent<Image>();
            }

            void Update()
            {
                time += Time.deltaTime * 3f;
                float pulse = (Mathf.Sin(time) + 1f) / 2f;
                float alpha = Mathf.Lerp(0.3f, 0.7f, pulse);
                float scale = Mathf.Lerp(1.0f, 1.2f, pulse);
                img.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
                transform.localScale = Vector3.one * scale;
            }
        }

        /// <summary>
        /// 勝利時のパーティクル風エフェクト
        /// </summary>
        void CreateVictoryParticle()
        {
            var particleGO = new GameObject("VictoryParticle");
            particleGO.transform.SetParent(canvas.transform, false);
            var particleRT = particleGO.AddComponent<RectTransform>();
            particleRT.anchorMin = new Vector2(0.5f, 0.5f);
            particleRT.anchorMax = new Vector2(0.5f, 0.5f);
            particleRT.anchoredPosition = new Vector2(UnityEngine.Random.Range(-200f, 200f), UnityEngine.Random.Range(-100f, 100f));
            particleRT.sizeDelta = new Vector2(30, 30);

            var particleImg = particleGO.AddComponent<Image>();
            particleImg.color = new Color(1f, 0.8f, 0.2f, 1f);
            particleImg.raycastTarget = false;

            var animator = particleGO.AddComponent<VictoryParticleAnimator>();
            animator.velocity = new Vector2(UnityEngine.Random.Range(-300f, 300f), UnityEngine.Random.Range(200f, 500f));
        }

        /// <summary>
        /// 勝利パーティクルのアニメーション
        /// </summary>
        public class VictoryParticleAnimator : MonoBehaviour
        {
            public Vector2 velocity;
            private RectTransform rt;
            private Image img;
            private float lifetime = 0f;

            void Start()
            {
                rt = GetComponent<RectTransform>();
                img = GetComponent<Image>();
            }

            void Update()
            {
                lifetime += Time.deltaTime;

                // 重力付き移動
                velocity.y -= 800f * Time.deltaTime;
                rt.anchoredPosition += velocity * Time.deltaTime;

                // 回転
                rt.Rotate(0, 0, 500f * Time.deltaTime);

                // フェードアウト
                float alpha = Mathf.Lerp(1f, 0f, lifetime / 2f);
                img.color = new Color(img.color.r, img.color.g, img.color.b, alpha);

                // 寿命で削除
                if (lifetime > 2f)
                {
                    Destroy(gameObject);
                }
            }
        }

        void CloseManaActionPanel()
        {
            manaActionPanel.SetActive(false);
            waitingForManaAction = false;

            // カード選択へ
            RenderHand();
            waitingForPlayer = true;
            turnStartTime = Time.realtimeSinceStartup;
        }

        void UpdateHeader()
        {
            headerText.text = $"Research TCG  |  参加者ID: {participantId}  |  ターン: {turnNumber}  |  スコア: You {playerScore} - AI {aiScore}  |  手札: {playerHand?.Count ?? 0}";
        }

        void UpdateManaDisplay()
        {
            manaText.text = $"マナ: <color=yellow>{playerMana}</color>";
        }

        void UpdatePhaseDisplay()
        {
            var phasePanelImg = phasePanel.GetComponent<Image>();
            switch (currentPhase)
            {
                case Phase.CardSelection:
                    phaseText.text = "カード選択フェーズ";
                    phasePanelImg.color = new Color(0.2f, 0.5f, 0.8f, 0.85f); // 青
                    infoText.text = "出すカードを選んでください";
                    break;
                case Phase.ManaGain:
                    phaseText.text = "マナ獲得フェーズ";
                    phasePanelImg.color = new Color(0.6f, 0.3f, 0.8f, 0.85f); // 紫
                    infoText.text = "捨て札を選んでマナを獲得してください";
                    break;
                case Phase.Battle:
                    phaseText.text = "バトルフェーズ";
                    phasePanelImg.color = new Color(0.8f, 0.3f, 0.3f, 0.85f); // 赤
                    infoText.text = "カードバトル！";
                    break;
            }
        }

        Color GetCardColor(string type)
        {
            return type.ToLower() switch
            {
                "fire" => new Color(0.9f, 0.3f, 0.2f),      // 赤
                "water" => new Color(0.2f, 0.5f, 0.9f),     // 青
                "earth" => new Color(0.6f, 0.4f, 0.2f),     // 茶色
                "nature" => new Color(0.4f, 0.7f, 0.3f),    // 緑
                "air" => new Color(0.7f, 0.9f, 0.9f),       // 薄い水色
                "wind" => new Color(0.7f, 0.9f, 0.7f),      // 薄緑
                "lightning" => new Color(0.9f, 0.9f, 0.3f), // 黄色
                "dark" => new Color(0.3f, 0.2f, 0.4f),      // 紫
                "light" => new Color(0.95f, 0.95f, 0.8f),   // 白
                _ => new Color(0.5f, 0.5f, 0.5f)            // グレー
            };
        }

        /// <summary>
        /// 属性相性を判定（ポケモン式：全対応）
        /// 各属性は2つに強く、2つに弱い
        /// </summary>
        /// <returns>1=type1有利, -1=type2有利, 0=引き分け</returns>
        int GetTypeAdvantage(string type1, string type2)
        {
            string t1 = type1.ToLower();
            string t2 = type2.ToLower();

            // 同じ属性は引き分け
            if (t1 == t2) return 0;

            // 各属性が有利な相手を定義
            var advantages = new Dictionary<string, HashSet<string>>
            {
                { "fire", new HashSet<string> { "nature", "air" } },      // 火は自然と風に強い
                { "water", new HashSet<string> { "fire", "earth" } },     // 水は火と土に強い
                { "nature", new HashSet<string> { "water", "earth" } },   // 自然は水と土に強い
                { "earth", new HashSet<string> { "fire", "air" } },       // 土は火と風に強い
                { "air", new HashSet<string> { "water", "nature" } }      // 風は水と自然に強い
            };

            // type1が有利か確認
            if (advantages.TryGetValue(t1, out var t1Beats) && t1Beats.Contains(t2))
                return 1;

            // type2が有利か確認
            if (advantages.TryGetValue(t2, out var t2Beats) && t2Beats.Contains(t1))
                return -1;

            // 未定義の属性（wind, lightning等）
            return 0;
        }
    }

    // カードホバーエフェクト用コンポーネント
    public class CardHoverEffect : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Vector3 normalScale = Vector3.one;
        public Vector3 hoverScale = new Vector3(1.1f, 1.1f, 1f);
        private bool isHovering = false;
        private float animationSpeed = 8f;

        void Update()
        {
            Vector3 targetScale = isHovering ? hoverScale : normalScale;
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * animationSpeed);
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            isHovering = true;
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
        {
            isHovering = false;
        }
    }
}