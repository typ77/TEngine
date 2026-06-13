/*
 * EasingCore (https://github.com/setchi/EasingCore)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/EasingCore/blob/master/LICENSE)
 */

using UnityEngine;
using TEngine;

namespace GameLogic.UI.FancyScrollView.EasingCore
{
    // 注：Ease 枚举已统一使用 TEngine.Ease，本命名空间仅保留 EasingFunction 委托与 Easing 缓动函数实现。

    /// <summary>缓动函数委托：输入归一化时间 t∈[0,1]，返回缓动后的值。</summary>
    public delegate float EasingFunction(float t);

    public static class Easing
    {
        /// <summary>
        /// Gets the easing function
        /// </summary>
        /// <param name="type">Ease type</param>
        /// <returns>Easing function</returns>
        public static EasingFunction Get(TEngine.Ease type)
        {
            switch (type)
            {
                case TEngine.Ease.Linear: return linear;
                case TEngine.Ease.InBack: return inBack;
                case TEngine.Ease.InBounce: return inBounce;
                case TEngine.Ease.InCirc: return inCirc;
                case TEngine.Ease.InCubic: return inCubic;
                case TEngine.Ease.InElastic: return inElastic;
                case TEngine.Ease.InExpo: return inExpo;
                case TEngine.Ease.InQuad: return inQuad;
                case TEngine.Ease.InQuart: return inQuart;
                case TEngine.Ease.InQuint: return inQuint;
                case TEngine.Ease.InSine: return inSine;
                case TEngine.Ease.OutBack: return outBack;
                case TEngine.Ease.OutBounce: return outBounce;
                case TEngine.Ease.OutCirc: return outCirc;
                case TEngine.Ease.OutCubic: return outCubic;
                case TEngine.Ease.OutElastic: return outElastic;
                case TEngine.Ease.OutExpo: return outExpo;
                case TEngine.Ease.OutQuad: return outQuad;
                case TEngine.Ease.OutQuart: return outQuart;
                case TEngine.Ease.OutQuint: return outQuint;
                case TEngine.Ease.OutSine: return outSine;
                case TEngine.Ease.InOutBack: return inOutBack;
                case TEngine.Ease.InOutBounce: return inOutBounce;
                case TEngine.Ease.InOutCirc: return inOutCirc;
                case TEngine.Ease.InOutCubic: return inOutCubic;
                case TEngine.Ease.InOutElastic: return inOutElastic;
                case TEngine.Ease.InOutExpo: return inOutExpo;
                case TEngine.Ease.InOutQuad: return inOutQuad;
                case TEngine.Ease.InOutQuart: return inOutQuart;
                case TEngine.Ease.InOutQuint: return inOutQuint;
                case TEngine.Ease.InOutSine: return inOutSine;
                // Custom / Default 及未知值退化为线性
                default: return linear;
            }

            float linear(float t) => t;

            float inBack(float t) => t * t * t - t * Mathf.Sin(t * Mathf.PI);

            float outBack(float t) => 1f - inBack(1f - t);

            float inOutBack(float t) =>
                t < 0.5f
                    ? 0.5f * inBack(2f * t)
                    : 0.5f * outBack(2f * t - 1f) + 0.5f;

            float inBounce(float t) => 1f - outBounce(1f - t);

            float outBounce(float t) =>
                t < 4f / 11.0f ?
                    (121f * t * t) / 16.0f :
                t < 8f / 11.0f ?
                    (363f / 40.0f * t * t) - (99f / 10.0f * t) + 17f / 5.0f :
                t < 9f / 10.0f ?
                    (4356f / 361.0f * t * t) - (35442f / 1805.0f * t) + 16061f / 1805.0f :
                    (54f / 5.0f * t * t) - (513f / 25.0f * t) + 268f / 25.0f;

            float inOutBounce(float t) =>
                t < 0.5f
                    ? 0.5f * inBounce(2f * t)
                    : 0.5f * outBounce(2f * t - 1f) + 0.5f;

            float inCirc(float t) => 1f - Mathf.Sqrt(1f - (t * t));

            float outCirc(float t) => Mathf.Sqrt((2f - t) * t);

            float inOutCirc(float t) =>
                t < 0.5f
                    ? 0.5f * (1 - Mathf.Sqrt(1f - 4f * (t * t)))
                    : 0.5f * (Mathf.Sqrt(-((2f * t) - 3f) * ((2f * t) - 1f)) + 1f);

            float inCubic(float t) => t * t * t;

            float outCubic(float t) => inCubic(t - 1f) + 1f;

            float inOutCubic(float t) =>
                t < 0.5f
                    ? 4f * t * t * t
                    : 0.5f * inCubic(2f * t - 2f) + 1f;

            float inElastic(float t) => Mathf.Sin(13f * (Mathf.PI * 0.5f) * t) * Mathf.Pow(2f, 10f * (t - 1f));

            float outElastic(float t) => Mathf.Sin(-13f * (Mathf.PI * 0.5f) * (t + 1)) * Mathf.Pow(2f, -10f * t) + 1f;

            float inOutElastic(float t) =>
                t < 0.5f
                    ? 0.5f * Mathf.Sin(13f * (Mathf.PI * 0.5f) * (2f * t)) * Mathf.Pow(2f, 10f * ((2f * t) - 1f))
                    : 0.5f * (Mathf.Sin(-13f * (Mathf.PI * 0.5f) * ((2f * t - 1f) + 1f)) * Mathf.Pow(2f, -10f * (2f * t - 1f)) + 2f);

            float inExpo(float t) => Mathf.Approximately(0.0f, t) ? t : Mathf.Pow(2f, 10f * (t - 1f));

            float outExpo(float t) => Mathf.Approximately(1.0f, t) ? t : 1f - Mathf.Pow(2f, -10f * t);

            float inOutExpo(float v) =>
                Mathf.Approximately(0.0f, v) || Mathf.Approximately(1.0f, v)
                    ? v
                    : v < 0.5f
                        ?  0.5f * Mathf.Pow(2f, (20f * v) - 10f)
                        : -0.5f * Mathf.Pow(2f, (-20f * v) + 10f) + 1f;

            float inQuad(float t) => t * t;

            float outQuad(float t) => -t * (t - 2f);

            float inOutQuad(float t) =>
                t < 0.5f
                    ?  2f * t * t
                    : -2f * t * t + 4f * t - 1f;

            float inQuart(float t) => t * t * t * t;

            float outQuart(float t)
            {
                var u = t - 1f;
                return u * u * u * (1f - t) + 1f;
            }

            float inOutQuart(float t) =>
                t < 0.5f
                    ? 8f * inQuart(t)
                    : -8f * inQuart(t - 1f) + 1f;

            float inQuint(float t) => t * t * t * t * t;

            float outQuint(float t) => inQuint(t - 1f) + 1f;

            float inOutQuint(float t) =>
                t < 0.5f
                    ? 16f * inQuint(t)
                    : 0.5f * inQuint(2f * t - 2f) + 1f;

            float inSine(float t) => Mathf.Sin((t - 1f) * (Mathf.PI * 0.5f)) + 1f;

            float outSine(float t) => Mathf.Sin(t * (Mathf.PI * 0.5f));

            float inOutSine(float t) => 0.5f * (1f - Mathf.Cos(t * Mathf.PI));
        }
    }
}
