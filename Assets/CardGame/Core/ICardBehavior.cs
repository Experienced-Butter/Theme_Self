namespace CardGame.Core
{
    /// <summary>
    /// 卡牌「生效」的统一接口。每张卡牌实体上挂载若干实现该接口的组件，
    /// 打出时按 ResolutionOrder 升序依次执行。
    /// </summary>
    public interface ICardBehavior
    {
        /// <summary>结算顺序，数值小的先执行。</summary>
        int ResolutionOrder { get; }

        /// <summary>结算本次效果。</summary>
        void Resolve(CardPlayContext context);

        /// <summary>人类可读的效果描述，供卡面与战报使用。</summary>
        string Describe();
    }
}
