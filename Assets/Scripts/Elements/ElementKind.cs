// Все элементы мира. Имя значения совпадает с именем спрайта в нижнем регистре: Key -> key_0, key_1, key_2.
public enum ElementKind {
    None = 0,

    Hero,
    Ghost,
    Slime,
    Bat,
    Frog,
    Bird,

    Grass,
    Water,
    Lava,
    Ice,
    Sand,
    Wall,

    Rock,
    Tree,
    Bush,
    Flower,
    Mushroom,
    Cactus,
    Cloud,
    Crystal,

    Key,
    Door,
    Chest,
    Box,
    Barrel,
    Gem,
    Coin,
    Star,
    Heart,
    Skull,

    Bomb,
    Torch,
    Book,
    Potion,
    Apple,
    Egg,
    Bone,

    Button,
    Lever,
    Gear,
    Spring,
    Spikes,
    Arrow,
    Portal,
    Magnet,
    Bell,

    Sun,
    Moon,
    Lightning,
    Fire,

    // Новые виды добавляем только в конец: номера сериализованы в легенде карты.
    Throne,
}
