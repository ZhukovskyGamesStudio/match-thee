using UnityEngine;

// Игровой экран вписан в центр окна, по краям остаются поля под интерфейс.
public static class ScreenFrame {
    public const float Margin = 0.1f; // доля окна под поле с каждой стороны

    // Прямоугольник игрового экрана в пикселях окна (начало координат — левый нижний угол).
    public static Rect InnerRect(float windowWidth, float windowHeight, float aspect) {
        float availableWidth = windowWidth * (1f - Margin * 2f);
        float availableHeight = windowHeight * (1f - Margin * 2f);
        float width = availableWidth;
        float height = width / aspect;
        if (height > availableHeight) {
            height = availableHeight;
            width = height * aspect;
        }

        return new Rect((windowWidth - width) / 2f, (windowHeight - height) / 2f, width, height);
    }
}
