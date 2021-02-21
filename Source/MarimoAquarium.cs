using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor.Marimo {
    public class Game {
        private const double SaveWaitTimeLength = 1.0;

        private SaveData saveData_ = null;
        
        private Aquarium aquarium_ = new Aquarium();
        private Actor actor_ = new Actor();

        public string actorName { 
            get => actor_.name;
            set {
                if (actor_.name != value) {
                    actor_.name = value;
                    bookingSave_ = true;
                }
            }
        }
        public string actorSize => GameUtil.SizeBaseString(actor_.sizeBase);
        public string water => GameUtil.PercentString(aquarium_.water);
        public string elapsedTime => GameUtil.TickString(latestTicks_ - saveData_.startTicks);
        public string aqualiumName => aquarium_.name;

        public Vector2 rectSize { get => aquarium_.rectSize; set => aquarium_.rectSize = value; }
        public Vector2 actorTarget { get => actor_.target; set => actor_.target = value; }

        public bool canUpgradeAquarium => aquarium_.CanUpgrade(actor_.sizeBase);
        public bool canCleanWater => aquarium_.canCleanWater;

        public event Action<float, float> onDrawWater {
            add => aquarium_.onDrawWater += value;
            remove => aquarium_.onDrawWater -= value;
        }
        public event Action<Vector2, float> onDrawMarimo {
            add => actor_.onDrawMarimo += value;
            remove => actor_.onDrawMarimo -= value;
        }

        private string saveFilePath_ = "";
        private long latestTicks_ = 0;
        private double saveWaitTime_ = 0;
        private bool bookingSave_ = false;

        /// <summary>
        /// ゲームの初期化
        /// </summary>
        public void Init(string saveFilePath) {
            saveFilePath_ = saveFilePath;

            actor_.InAquarium(aquarium_);

            DataLoad();

            UpdateAquarium(SyncTime());
        }

        /// <summary>
        /// フレーム更新
        /// </summary>
        public void Update() {
            var deltaTime = SyncTime();
            UpdateAquarium(deltaTime);

            aquarium_.Update(deltaTime);
            actor_.Update(deltaTime);

            
            if (bookingSave_) {
                saveWaitTime_ += deltaTime;
                if (saveWaitTime_ >= SaveWaitTimeLength) {
                    DataSave();
                    saveWaitTime_ = 0;
                    bookingSave_ = false;
                }
            }
        }

        /// <summary>
        /// 水をきれいにする
        /// </summary>
        public void CleanWater() {
            if (aquarium_.TryCleanWater()) {
                bookingSave_ = true;
            }
        }

        /// <summary>
        /// 水槽をアップグレードする
        /// </summary>
        public void UpgradeAquarium() {
            if (aquarium_.TryUpgradeAquarium(actor_.sizeBase)) {
                bookingSave_ = true;
                actor_.StartUpgradeAnimation();
            }
        }

        /// <summary>
        /// 描画
        /// </summary>
        public void Draw() {
            actor_.Draw();
            aquarium_.Draw();
        }

        /// <summary>
        /// ゲーム終了処理
        /// </summary>
        public void End() {
            DataSave();
        }
        
        /// <summary>
        /// データをセーブする
        /// </summary>
        private void DataSave() {
            aquarium_.Save(saveData_);
            actor_.Save(saveData_);

            saveData_.latestTicks = latestTicks_;

            saveData_.Save(saveFilePath_);
        }

        /// <summary>
        /// データをロードする
        /// </summary>
        private void DataLoad() {
            saveData_ = SaveData.Load(saveFilePath_);

            latestTicks_ = saveData_.latestTicks;

            actor_.Load(saveData_);
            aquarium_.Load(saveData_);
        }

        /// <summary>
        /// ゲーム時間を進める
        /// </summary>
        /// <returns>前回更新からの時間</returns>
        private double SyncTime() {
            var tmp = latestTicks_;
            latestTicks_ = DateTime.Now.Ticks;
            return (latestTicks_ - tmp) * 0.0000001;
        }

        /// <summary>
        /// 水槽の時間を進める
        /// </summary>
        private void UpdateAquarium(double deltaTime) {
            double beforeWater = aquarium_.water;
            if (aquarium_.waterTimeLimit >= deltaTime) {
                aquarium_.DirtyWater(deltaTime);
                actor_.Glow((beforeWater + aquarium_.water) / 2, deltaTime);
            } else {
                actor_.Glow(beforeWater / 2, aquarium_.waterTimeLimit);
                aquarium_.DirtyWater(deltaTime);
            }
        }

        /// <summary>
        /// ツイート
        /// </summary>
        public void Tweet() {
            Application.OpenURL("https://twitter.com/intent/tweet?text=" + Uri.EscapeDataString(MakeTweetText()));
        }

        /// <summary>
        /// ツイート文章の作成
        /// </summary>
        private string MakeTweetText() {
            return $"Unityで、まりもの「{actorName}」を {actorSize} まで育てました。";
        }
    }

    /// <summary>
    /// まりもさん
    /// </summary>
    public class Actor {
        private const double glowSizePerSec = 1.2 / 86400.0;
        private const double upgradeAnimeLength_ = 2.0;

        public double sizeBase { get; private set; } = 0;
        public string name { get; set; } = "";
        public Vector2 target { get; set; }

        private Vector2 pos_  = Vector2.zero;
        private float drawSize_ = 0;
        private Aquarium house_ = null;
        private float velocity_ = 0;

        private float drawSizeOld_ = 0;
        private double upgradeAnimeTime_ = upgradeAnimeLength_;

        public event Action<Vector2, float> onDrawMarimo;

        /// <summary>
        /// 水槽をアップグレードしたときのアニメーションを再生する
        /// </summary>
        public void StartUpgradeAnimation() {
            upgradeAnimeTime_ = 0;
            drawSizeOld_ = drawSize_;
        }
        
        /// <summary>
        /// 水槽にまりもを入れる
        /// </summary>
        public void InAquarium(Aquarium aquarium) {
            house_ = aquarium;
            pos_ = house_.rectSize / 2;
        }

        /// <summary>
        /// 指定時間分まりもを成長させる
        /// </summary>
        public void Glow(double waterRate, double deltaTime) {
            sizeBase = Math.Min(sizeBase + glowSizePerSec * deltaTime * waterRate, house_?.capacityBase ?? 0);
        }

        /// <summary>
        /// フレーム更新
        /// </summary>
        public void Update(double deltaTime) {
            if (house_ == null) return;

            if (house_.capacityBase != double.PositiveInfinity) {
                drawSize_ = (float)(
                    Math.Max(Math.Pow(10, Math.Min(sizeBase - house_.capacityBase, 0)), 0.005) *
                    (100 + Math.Max(40.0 * house_.capacityBase, 0)));
            } else {
                drawSize_ = (float)Math.Min(sizeBase, 960);
            }

            // アニメーション
            if (upgradeAnimeTime_ < upgradeAnimeLength_) {
                upgradeAnimeTime_ += deltaTime;
                drawSize_ = Mathf.Lerp(drawSizeOld_, drawSize_, (float)GameUtil.easeOutElastic(upgradeAnimeTime_ / upgradeAnimeLength_));
            }

            // 移動
            var dist = Vector2.Distance(pos_, target);
            dist = Mathf.SmoothDamp(dist, 0, ref velocity_, 5.0f, 100.0f, (float)deltaTime);
            pos_ = target - (target - pos_).normalized * dist;
            pos_ += Vector2.up * 5.0f;

            // 壁
            var wallSize = house_.rectSize;
            if (wallSize.x <= drawSize_ * 2) {
                pos_.x = wallSize.x / 2;
            } else {
                pos_.x = Mathf.Clamp(pos_.x, drawSize_, wallSize.x - drawSize_);
            }

            if (wallSize.y <= drawSize_ * 2) {
                pos_.y = wallSize.y / 2;
            } else {
                pos_.y = Mathf.Clamp(pos_.y, drawSize_ + (1 - house_.waterAmount) * (wallSize.y - drawSize_ * 2), wallSize.y - drawSize_);
            }
        }

        /// <summary>
        /// 描画更新
        /// </summary>
        public void Draw() {
            if (house_ == null) return;
            onDrawMarimo?.Invoke(pos_, drawSize_);
        }

        public void Load(SaveData save) {
            sizeBase = save.sizeBase;
            name = save.name;
        }

        public void Save(SaveData save) {
            save.sizeBase = sizeBase;
            save.name = name;
        }
    }

    /// <summary>
    /// 水槽
    /// </summary>
    public class Aquarium {
        public class Type {
            public string name { get; private set; }
            public double capacityBase { get; private set; }

            public Type(string name, double capacityBase) {
                this.name = name;
                this.capacityBase = capacityBase;
            }
        }

        private readonly Type[] AquariumTypes = {
            new Type(capacityBase: -2.0, name: "シケンカン(1cm)"),
            new Type(capacityBase: -1.0, name: "ビン(10cm)"),
            new Type(capacityBase:  0.0, name: "ヨクソウ(1m)"),
            new Type(capacityBase:  2.0, name: "スイゾクカン(100m)"),
            new Type(capacityBase:  4.0, name: "ミズウミ(10km)"),
            new Type(capacityBase:  6.0, name: "ウミ(1,000km)"),
            new Type(capacityBase:  7.0, name: "チキュウ(10,000km)"),
            new Type(capacityBase:  9.0, name: "タイヨウ(1,000,000km)"),
            new Type(capacityBase: 13.0, name: "タイヨウケイ(10^10km)"),
            new Type(capacityBase: 17.0, name: "セイダン(10^14km)"),
            new Type(capacityBase: 23.0, name: "ギンガグン(10^20km)"),
            new Type(capacityBase: 27.0, name: "ウチュウ(10^24km)"),
            new Type(capacityBase: double.PositiveInfinity, name: "???")
        };
        private const double WaterLifeTime = 86400.0 * 4.2;
        private const double CleanAnimeLength = 3.0;

        public double water { get; private set; } = 0;
        public Vector2 rectSize { get; set; } = Vector2.zero;
        public float waterAmount { get; private set; } = 0;

        private int  grade_ = 0;
        private Type type_ = null;

        private double cleanAnimeTime = CleanAnimeLength;
        private double waterOld = 0;
        private double waterDisplay = 0;

        public event Action<float, float> onDrawWater;

        public string name => type_?.name ?? "---";
        public double capacityBase => type_?.capacityBase ?? 0;
        public double waterTimeLimit => water * WaterLifeTime;
        public bool canCleanWater => water <= 0.99;

        /// <summary>
        /// 水槽のグレードを設定する
        /// </summary>
        private void SetGrade(int grade) {
            grade_ = Mathf.Clamp(grade, 0, AquariumTypes.Length);
            type_ = AquariumTypes[grade];
        }

        /// <summary>
        /// 水槽を1段階アップグレードする
        /// </summary>
        private void Upgrade() {
            SetGrade(grade_ + 1);
        }

        /// <summary>
        /// 水をきれいにするアニメーションを再生する
        /// </summary>
        public void StartCleanAnimation() {
            waterOld = water;
            cleanAnimeTime = 0;
        }

        /// <summary>
        /// 水槽をアップグレードできるか
        /// </summary>
        public bool CanUpgrade(double sizeBase) => (sizeBase >= type_.capacityBase) && (grade_ < AquariumTypes.Length - 1);

        /// <summary>
        /// 水を汚す(時間を進める)
        /// </summary>
        public void DirtyWater(double deltaTime) {
            water = Math.Max(water - deltaTime / WaterLifeTime, 0);
        }

        /// <summary>
        /// 水をきれいにする
        /// 水をきれいにできなかった(する必要がない)場合はfalseが返る
        /// </summary>
        public bool TryCleanWater() {
            if (!canCleanWater) return false;
            StartCleanAnimation();
            water = 1;
            return true;
        }

        /// <summary>
        /// 水槽のアップグレードをする
        /// 水槽をアップグレードできない場合はfalseが返る
        /// </summary>
        public bool TryUpgradeAquarium(double sizeBase) {
            if (!CanUpgrade(sizeBase)) return false;

            Upgrade();
            return true;
        }

        /// <summary>
        /// フレーム更新
        /// </summary>
        public void Update(double deltaTime) {
            if (cleanAnimeTime <= CleanAnimeLength) {
                cleanAnimeTime += deltaTime;
                double t = cleanAnimeTime / CleanAnimeLength;
                if (t < 0.5) {
                    t = 1 - GameUtil.EaseOutCubic(t * 2);
                    waterDisplay = waterOld;
                } else {
                    t = GameUtil.EaseOutCubic((t - 0.5) * 2);
                    waterDisplay = water;
                }
                waterAmount = (float)t;
            } else {
                waterDisplay = water;
                waterAmount = 1.0f;
            }
        }

        /// <summary>
        /// 描画更新
        /// </summary>
        public void Draw() {
            onDrawWater(waterAmount, (float)waterDisplay);
        }

        public void Load(SaveData save) {
            SetGrade(save.aqualiumGrade);
            water = save.water;
        }

        public void Save(SaveData save) {
            save.aqualiumGrade = grade_;
            save.water = water;
        }
    }

    /// <summary>
    /// エディタのウィンドウクラス
    /// </summary>
    public class MarimoAquarium : EditorWindow {
        private Game game_ = null;
        
        private bool initialized => game_ != null;

        /// <summary>
        /// ウィンドウの生成 (エディタ上部のメニューを追加)
        /// </summary>
        [MenuItem("Window/Marimo/まりもの水槽を覗く")]
        private static void Create() {
            var window = GetWindow<MarimoAquarium>("Marimo");
            window.Init();
        }

        private void OnEnable() {
            Init();
        }

        /// <summary>
        /// 初期化
        /// </summary>
        private void Init() {
            if (initialized) return;

            game_ = new Game();
            
            game_.onDrawMarimo += DrawMarimo;
            game_.onDrawWater += DrawWater;

            game_.rectSize = position.size;

            var path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this))) + "/Marimo.dat";
            game_.Init(path);

            wantsMouseMove = true;
        }

        /// <summary>
        /// ウィンドウのGUIを生成
        /// </summary>
        private void OnGUI() {
            if (!initialized) Init();

            if (Event.current.type == EventType.MouseMove) {
                game_.actorTarget = Event.current.mousePosition;
                return;
            }

            game_.Draw();

            using (new GUILayout.VerticalScope()) {
                using (new GUILayout.VerticalScope()) {
                    game_.actorName = EditorGUILayout.TextField("名前", game_.actorName);
                    EditorGUILayout.LabelField("大きさ", game_.actorSize);
                    EditorGUILayout.LabelField("水質", game_.water);
                    EditorGUILayout.LabelField("水槽", game_.aqualiumName);
                    EditorGUILayout.LabelField("育て始めてから", game_.elapsedTime);
                }
                using (new GUILayout.VerticalScope(GUILayout.ExpandHeight(true))) {

                }

                using (new GUILayout.HorizontalScope()) {
                    EditorGUI.BeginDisabledGroup(!game_.canCleanWater);
                    if (GUILayout.Button("水を交換する")) {
                        game_.CleanWater();
                    }
                    EditorGUI.EndDisabledGroup();
                    EditorGUI.BeginDisabledGroup(!game_.canUpgradeAquarium);
                    if (GUILayout.Button("水槽を大きく")) {
                        game_.UpgradeAquarium();
                    }
                    EditorGUI.EndDisabledGroup();

                    if (GUILayout.Button("t", GUILayout.Width(30))) {
                        game_.Tweet();
                    }
                }
            }
        }

        /// <summary>
        /// まりもの描画
        /// </summary>
        private void DrawMarimo(Vector2 pos, float size) {
            Handles.color = new Color(0.4f, 0.6f, 0.2f, 1.0f);
            Handles.DrawSolidDisc(pos, Vector3.forward, size);
        }

        /// <summary>
        /// 水の描画
        /// </summary>
        private void DrawWater(float heightRate, float water) {
            Handles.color = Color.white;
            Handles.DrawSolidRectangleWithOutline(
                new Rect(0, position.height - position.height * heightRate, position.width, position.height * heightRate), 
                Color.Lerp(new Color(0.5f, 0.5f, 0.2f, 0.2f), new Color(0.1f, 0.5f, 0.7f, 0.2f), water), 
                Color.clear
            );
        }

        /// <summary>
        /// 更新処理
        /// </summary>
        private void OnInspectorUpdate() {
            if (!initialized) Init(); 

            game_.rectSize = position.size;
            game_.Update();
            Repaint();
        }

        /// <summary>
        /// 終了処理
        /// </summary>
        private void OnDestroy() {
            if (!initialized) return;

            game_.End();
            game_ = null;
        }
    }


    /// <summary>
    /// ゲームに関する便利関数とか
    /// </summary>
    public static class GameUtil {
        /// <summary>
        /// Log10で保持されている大きさをフォーマットする
        /// </summary>
        public static string SizeBaseString(double sizeBase) {
            double size = 0;

            if (sizeBase < 18) {
                size = Math.Pow(10, sizeBase);
                int nm = (int)(size * 1000000000 % 1000);
                int mcm = (int)(size * 1000000 % 1000);
                int mm = (int)(size * 1000 % 10);
                if (sizeBase < -2) return $"{mm}mm{mcm}μm{nm}nm";

                int cm = (int)(size * 100 % 100);
                if (sizeBase < 0) return $"{cm}cm{mm}mm{mcm}μm";

                int m = (int)(size % 1000);
                if (sizeBase < 3) return $"{m}m{cm}cm{mm}mm";

                double km = size / 1000;
                if (sizeBase < 9) return $"{km:N0}km{m}m{cm}cm{mm}mm";
                if (sizeBase < 12) return $"{km:N0}km{m}m{cm}cm";
                if (sizeBase < 15) return $"{km:N0}km{m}m";
                return $"{km:N0}km";
            }

            return $"{Math.Pow(10, sizeBase % 1.0d):F6} x 10^{Math.Floor(sizeBase - 3):N0} km";
        }

        /// <summary>
        /// パーセント表記にフォーマット
        /// </summary>
        public static string PercentString(double percent) {
            return $"{(int)Math.Ceiling(percent * 100)}%";
        }

        /// <summary>
        /// ティック(100ナノ秒単位の時間)をフォーマット
        /// </summary>
        public static string TickString(long tick) {
            long sec = tick / 10000000;

            long s = sec % 60;
            if (sec < 60) return $"{s} 秒";

            long m = sec / 60 % 60;
            if (sec < 3600) return $"{m} 分 {s} 秒";

            long h = sec / 3600 % 24;
            if (sec < 86400) return $"{h} 時間 {m} 分";

            long d = sec % 31556952 / 86400;
            if (sec < 864000) return $"{d} 日 {h} 時間";
            if (sec < 31556952) return $"{d} 日";

            long y = sec / 31556952;
            return $"{y} 年 {d} 日";
        }

        /// <summary>
        /// イージング (EaseOutCubic)
        /// </summary>
        public static double EaseOutCubic(double t) {
            t = t - 1;
            return -1 * (t * t * t * t - 1);
        }

        /// <summary>
        /// イージング (EaseOutElastic)
        /// </summary>
        public static double easeOutElastic(double t) {
            const double c4 = (2 * Math.PI) / 3;
            return (t == 0) ? 0 : ((t == 1) ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1);
        }
    }

    /// <summary>
    /// セーブデータ
    /// </summary>
    public class SaveData {

        public string name;
        public double sizeBase;
        public double water;
        public int aqualiumGrade;
        public long startTicks;
        public long latestTicks;


        /// <summary>
        /// データの初期化
        /// </summary>
        private void Init() {
            name = "まりもさん";
            sizeBase = -3; // 1mm
            water = 0.5;   // 50%
            aqualiumGrade = 0;
            startTicks  = DateTime.Now.Ticks;
            latestTicks = startTicks;
        }

        /// <summary>
        /// パラメータのシリアライズ
        /// </summary>
        /// <param name="bw"></param>
        private void SerializeField(BinaryWriter bw) {
            bw.Write(name);
            bw.Write(sizeBase);
            bw.Write(water);
            bw.Write(aqualiumGrade);
            bw.Write(startTicks);
            bw.Write(latestTicks);
        }

        /// <summary>
        /// パラメータのデシリアライズ
        /// </summary>
        private void DeserializeField(BinaryReader br) {
            name = br.ReadString();
            sizeBase = br.ReadDouble();
            water = br.ReadDouble();
            aqualiumGrade = br.ReadInt32();
            startTicks = br.ReadInt64();
            latestTicks = br.ReadInt64();
        }


        /// <summary>
        /// セーブ
        /// </summary>
        public void Save(string path) {
            try {
                byte[] binary = Serialize();
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                    fs.Write(binary, 0, binary.Length);
                }
            } catch (Exception e) {
                Debug.Log($"セーブ失敗 : {e.Message}");
            }
        }

        /// <summary>
        /// ロード
        /// </summary>
        public static SaveData Load(string path) {
            SaveData ret = new SaveData();

            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists) {
                try {
                    using (var fs = fileInfo.OpenRead()) {
                        ret.Deserialize(fs);
                    }
                } catch (Exception e) {
                    Debug.Log($"ロード失敗 : {e.Message}");
                    ret.Init();
                }
            } else {
                // セーブデータ新規作成
                ret.Init();
            }

            return ret;
        }

        /// <summary>
        /// シリアライズ
        /// </summary>
        private byte[] Serialize() {
            using (var ms = new MemoryStream()) {
                using (var bw = new BinaryWriter(ms)) {
                    bw.Seek(HeaderSize, SeekOrigin.Begin);

                    SerializeField(bw);

                    var bs = new SHA256CryptoServiceProvider().ComputeHash(ms.GetBuffer(), HeaderSize, (int)ms.Length - HeaderSize);
                    bw.Seek(0, SeekOrigin.Begin);
                    bw.Write((int)ms.Length);
                    bw.Write(bs, 0, 32);
                }
                return Encrypt(ms.GetBuffer());
            }
        }

        /// <summary>
        /// デシリアライズ
        /// </summary>
        private void Deserialize(FileStream fs) {
            byte[] binary = new byte[fs.Length];

            fs.Read(binary, 0, binary.Length);
            binary = Decrypt(binary);

            using (var ms = new MemoryStream(binary)) {
                using (var br = new BinaryReader(ms)) {

                    byte[] hashDat = new byte[32];
                    byte[] hashAns = new byte[32];

                    int size = br.ReadInt32();

                    // データの整合性チェック
                    hashDat = br.ReadBytes(32);
                    hashAns = new SHA256CryptoServiceProvider().ComputeHash(binary, HeaderSize, size - HeaderSize);

                    if (!hashAns.SequenceEqual(hashDat)) {
                        // データが不正
                        throw new Exception("ハッシュ値が違います");
                    }

                    DeserializeField(br);
                };
            }
        }

        /// <summary>
        /// ヘッダーのバイトサイズ
        /// </summary>
        private static int HeaderSize => 32 + sizeof(int); // SHA256 + DataLength

        /// <summary>
        /// AESでバイト配列を暗号化する
        /// </summary>
        private byte[] Encrypt(byte[] binary) {
            using (ICryptoTransform encrypt = getServiceProvider().CreateEncryptor()) {
                return encrypt.TransformFinalBlock(binary, 0, binary.Length);
            }
        }

        /// <summary>
        /// AESで暗号化されたバイト配列を複合する
        /// </summary>
        private byte[] Decrypt(byte[] binary) {
            using (ICryptoTransform decrypt = getServiceProvider().CreateDecryptor()) {
                return decrypt.TransformFinalBlock(binary, 0, binary.Length);
            }
        }

        /// <summary>
        /// 暗号化に必要な情報を取得する
        /// おもいっくそ書いてあるし暗号化の意味がないよ
        /// </summary>
        private AesCryptoServiceProvider getServiceProvider() {
            return new AesCryptoServiceProvider {
                BlockSize = 128,
                KeySize = 128,
                IV  = Encoding.UTF8.GetBytes("Marimomarimomari"),              
                Key = Encoding.UTF8.GetBytes("veryhappyMRMtaso"),
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7,
            };
        }
    }
}
