namespace TEngine.Editor.ExcelTool
{
    public interface ITypeParser
    {
        object Parse(string type, string data, ParseContext ctx, int depth);
    }
}