using System.Collections.Generic;
using Luban;
using UnityEngine.Scripting;

namespace TEngine
{
    /// <summary>
    /// 此类非自动生成类，但是也请切勿手动修改
    /// 此类为统一的多语言获取配置类
    /// </summary>
    [Preserve]
    public class LanguageConfigItem
    {
        public System.Int32 Id { get; private set; }
        public System.String Default { get; private set; }
        
        public LanguageConfigItem (ByteBuf byteBuf)
        {
            Id = byteBuf.ReadInt();
            Default = byteBuf.ReadString();
        }
    }

    [Preserve]
    public class LanguageConfig : TableBaseTemp<System.Int32, LanguageConfigItem>
    {
        protected override System.Int32 ReadKey(ByteBuf byteBuf)
        {
            return byteBuf.ReadInt();
        }
        protected override LanguageConfigItem CreateItem(ByteBuf byteBuf)
        {
            return new LanguageConfigItem(byteBuf);
        }
    }
}