using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;
namespace Game.Game.UI.Components
{
    

    public class CustomTextDrawing : Script
    {
        public const int MaxLineWidth = 200; // 定义最大行宽
        public  Font font; // 字体资源
        public string text = "这是一段很长的文本，需要自动换行。这是一段很长的文本，需要自动换行。";
        public float lineHeight = 20f; // 行高
        public Vector2 position = new Vector2(10, 10); // 文本绘制位置

        public override void OnAwake()
        {
            base.OnAwake();
       
            
            DrawWrappedText(text);
        }

        private void DrawWrappedText(string text)
        {
            string[] words = text.Split(' '); // 按空格分割单词
            string currentLine = "";
            float lineWidth = 0f;
            float x = position.X;
            float y = position.Y;

            foreach (string word in words)
            {
                float wordWidth = font.MeasureText(word).X; // 获取单词宽度
                if (lineWidth + wordWidth > MaxLineWidth) // 如果加上当前单词后超出行宽，换行
                {
                    DrawText(currentLine, x, y); // 绘制当前行
                    currentLine = ""; // 重置当前行内容
                    y += lineHeight; // 移动到新的一行
                    x = position.X; // 重置X坐标到开始位置
                }
                currentLine += word + " "; // 添加单词到当前行，并保留一个空格
                lineWidth += wordWidth + font.MeasureText(" ").X; // 更新行宽（包括空格）
            }
            DrawText(currentLine, x, y); // 绘制最后一行（如果有）
        }

        private void DrawText(string text, float x, float y)
        {
           // Graphics.(font, text, new Vector2(x, y), Color.White); // 绘制文本到屏幕
        }
    }

}
