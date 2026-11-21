using System;

namespace GabrielAutoclick.Models
{
    public class ClickPoint
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Index { get; set; } = 1;
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int DelayMs { get; set; } = 500;
        public MouseButtonType MouseButton { get; set; } = MouseButtonType.Left;
        public ClickActionType ClickAction { get; set; } = ClickActionType.Single;

        [System.Text.Json.Serialization.JsonIgnore]
        public string ButtonName => MouseButton switch
        {
            MouseButtonType.Right => "Direito",
            MouseButtonType.Middle => "Meio",
            _ => "Esquerdo"
        };

        [System.Text.Json.Serialization.JsonIgnore]
        public string ActionName => ClickAction switch
        {
            ClickActionType.Double => "Clique duplo",
            ClickActionType.HoldDown => "Pressionar",
            ClickActionType.HoldUp => "Soltar",
            _ => "Clique simples"
        };

        public ClickPoint Clone()
        {
            return new ClickPoint
            {
                Id = Guid.NewGuid().ToString(),
                Index = this.Index,
                X = this.X,
                Y = this.Y,
                DelayMs = this.DelayMs,
                MouseButton = this.MouseButton,
                ClickAction = this.ClickAction
            };
        }
    }
}
