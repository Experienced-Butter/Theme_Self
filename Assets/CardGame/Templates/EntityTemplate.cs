using UnityEngine;

namespace CardGame.Templates
{
    /// <summary>
    /// 所有模板实体的基类。模板 GameObject 在场景中应保持 SetActive(false)，
    /// 运行时通过 CreateRuntimeInstance 复制出「完全独立的临时实体」。
    ///
    /// 克隆语义保证（本任务的核心需求）：
    /// 1. Instantiate 深拷贝模板；克隆体不持有模板上的任何组件引用。
    /// 2. 克隆体立刻被重命名、挂到 parent 下、SetActive(true)。
    /// 3. 摘掉克隆体上的 EntityTemplate 标记组件，使其从「模板」变成「普通实体」。
    /// 4. 补上 EntityInstance 并分配全局唯一自增 instanceId。
    /// 5. 调用 OnAfterClone，由子类把序列化参数写入克隆体自己的组件（数组/引用类型必须重新分配），
    ///    因此克隆体与模板、克隆体与克隆体之间不存在共享可变状态。
    /// </summary>
    public abstract class EntityTemplate : MonoBehaviour
    {
        /// <summary>模板标识，克隆体通过 EntityInstance.templateId 记录来源。</summary>
        public string templateId;

        private static int _nextInstanceId;
        private static readonly object _instanceIdLock = new object();

        /// <summary>全局自增实例 ID 计数（已分配的最大值）。</summary>
        public static int NextInstanceId
        {
            get { return _nextInstanceId; }
            private set { _nextInstanceId = value; }
        }

        /// <summary>
        /// 克隆：Instantiate 深拷贝 → 激活 → 摘掉模板标记 → 打上 EntityInstance → 调用 OnAfterClone。
        /// 返回的 GameObject 已是与模板完全无关的独立运行时实体。
        /// </summary>
        public GameObject CreateRuntimeInstance(Transform parent, string instanceName)
        {
            // 深拷贝模板（不继承父节点，克隆体位置随后显式设定）。
            // 显式限定 UnityEngine.Object：本文件带 using System，避免 Object 名解析歧义。
            GameObject clone = UnityEngine.Object.Instantiate(gameObject);
            clone.name = instanceName;

            // 挂到目标父节点下（parent 为 null 时保持在场景根）并激活为运行时实体；
            // false = 不保留世界坐标，克隆体的 localPosition 归零，避免模板摆放位置影响生成点。
            clone.transform.SetParent(parent, false);
            clone.SetActive(true);

            DestroyTemplateMarker(clone);

            int instanceId = AllocateInstanceId();
            EntityInstance instance = clone.GetComponent<EntityInstance>();
            if (instance == null)
            {
                instance = clone.AddComponent<EntityInstance>();
            }
            instance.Initialize(templateId, instanceId);

            OnAfterClone(clone);
            return clone;
        }

        /// <summary>
        /// 子类在这里把模板的序列化参数写入克隆体上各自的组件
        /// （若 Instantiate 已足够可留空，但数组等引用类型必须重新分配）。
        /// </summary>
        protected virtual void OnAfterClone(GameObject clone)
        {
        }

        /// <summary>
        /// 摘掉克隆体上的全部 EntityTemplate 标记组件：运行时用 Destroy，编辑器用 DestroyImmediate。
        /// </summary>
        protected static void DestroyTemplateMarker(GameObject clone)
        {
            if (clone == null)
            {
                return;
            }

            EntityTemplate[] markers = clone.GetComponents<EntityTemplate>();
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null)
                {
                    continue;
                }
                if (Application.isPlaying)
                {
                    // 运行时：Destroy 会把实际移除推迟到当前帧末，但组件已进入销毁队列，
                    // 不会再被当作模板使用（模板本体是场景里另一个对象）。
                    UnityEngine.Object.Destroy(markers[i]);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(markers[i]);
                }
            }
        }

        private static int AllocateInstanceId()
        {
            lock (_instanceIdLock)
            {
                _nextInstanceId++;
                return _nextInstanceId;
            }
        }
    }
}
