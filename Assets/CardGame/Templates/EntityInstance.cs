using UnityEngine;

namespace CardGame.Templates
{
    /// <summary>
    /// 运行时实体的身份标记。模板 GameObject 上没有本组件；
    /// 由 EntityTemplate.CreateRuntimeInstance 克隆后补上，用于区分「模板」与「克隆体」。
    /// </summary>
    public class EntityInstance : MonoBehaviour
    {
        /// <summary>运行时自增唯一实例 ID。</summary>
        public int instanceId;

        /// <summary>来源模板的 templateId。</summary>
        public string templateId;

        /// <summary>克隆体恒为 true。</summary>
        public bool isRuntimeInstance;

        /// <summary>写入来源模板与唯一实例 ID，并标记本对象为运行时克隆体。</summary>
        public void Initialize(string templateId, int instanceId)
        {
            this.templateId = templateId;
            this.instanceId = instanceId;
            this.isRuntimeInstance = true;
        }
    }
}
