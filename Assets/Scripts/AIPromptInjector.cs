using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 提示词注入器：把模板里 {{占位符}} 替换成运行时生成的动态内容。
/// 设计目的（可扩展性）：提示词中"会变化的部分"全部抽象成占位符 + 注入函数，
/// 未来新增动态段时只需要：
///   1. 在 PromptTemplate.json 的模板里加一个 {{xxx}} 占位符；
///   2. 在 AIFreeDialogueFlow.BuildPromptMessages 里注册对应的注入函数；
/// 提示词模板与文案本身也放在 JSON（PromptTemplate.json）里，改文案不用改代码。
/// 注入函数的产出将来可以改为从别的 JSON 文件读取，注入机制本身不变。
/// </summary>
public static class AIPromptInjector
{
    private static readonly Dictionary<string, Func<string>> injectors = new Dictionary<string, Func<string>>();

    /// <summary>注册占位符 -> 内容生成函数（重复注册覆盖，注册时不调用函数）</summary>
    public static void Register(string key, Func<string> func)
    {
        injectors[key] = func;
    }

    /// <summary>把模板里所有已注册的 {{key}} 替换成注入函数的结果（多轮替换，支持占位符嵌套）</summary>
    public static string Inject(string template)
    {
        if (string.IsNullOrEmpty(template)) return template;
        string result = template;
        // 最多跑 5 轮：一轮替换后，值里新出现的 {{xx}}（比如输出规则里引用 {{emotionList}}）继续替换
        for (int round = 0; round < 5; round++)
        {
            bool changed = false;
            foreach (KeyValuePair<string, Func<string>> kv in injectors)
            {
                string token = "{{" + kv.Key + "}}";
                if (result.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    string value = kv.Value();
                    if (value == null) value = "";
                    result = result.Replace(token, value);
                    changed = true;
                }
            }
            if (!changed) break;
        }
        return result;
    }
}
