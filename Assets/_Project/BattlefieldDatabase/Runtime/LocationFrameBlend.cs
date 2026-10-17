using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12М: смешивание двух настроек обработки кадра — основа ключей
    // суток, зон места и переходов между стилями. Идёт по всем полям:
    // числа, цвета и векторы — плавно; галочки, выборы, картинки, кривые —
    // переключением на середине (для плавного появления эффекта держите
    // его включённым и меняйте силу). Результат пишется в свою копию:
    // данные мест и стилей не меняются.
    public static class LocationFrameBlend
    {
        private static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();

        public static T Lerp<T>(T a, T b, float t, T target = null) where T : class, new()
        {
            if (a == null && b == null) return null;
            target ??= new T();
            Blend(typeof(T), a ?? b, b ?? a, Mathf.Clamp01(t), target);
            return target;
        }

        // Копия без общих ссылок на вложенные классы (картинки и кривые — общие).
        public static T Copy<T>(T source, T target = null) where T : class, new() => Lerp(source, source, 0, target);

        private static FieldInfo[] Fields(Type type)
        {
            if (fields.TryGetValue(type, out FieldInfo[] cached)) return cached;
            cached = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(field => !field.IsNotSerialized && !field.IsInitOnly).ToArray();
            fields[type] = cached;
            return cached;
        }

        private static bool IsBlendClass(Type type) =>
            type.IsClass && type != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(type) &&
            type != typeof(AnimationCurve) && type != typeof(Gradient) && type.IsDefined(typeof(SerializableAttribute), false) &&
            type.GetConstructor(Type.EmptyTypes) != null;

        private static void Blend(Type type, object a, object b, float t, object target)
        {
            foreach (FieldInfo field in Fields(type))
                field.SetValue(target, Value(field.FieldType, field.GetValue(a), field.GetValue(b), t, field.GetValue(target)));
        }

        private static object Value(Type type, object a, object b, float t, object current)
        {
            if (a == null) a = b;
            if (b == null) b = a;
            if (a == null) return null;
            if (type == typeof(float)) return Mathf.Lerp((float)a, (float)b, t);
            if (type == typeof(int)) return Mathf.RoundToInt(Mathf.Lerp((int)a, (int)b, t));
            if (type == typeof(Color)) return Color.Lerp((Color)a, (Color)b, t);
            if (type == typeof(Vector2)) return Vector2.Lerp((Vector2)a, (Vector2)b, t);
            if (type == typeof(Vector3)) return Vector3.Lerp((Vector3)a, (Vector3)b, t);
            if (type == typeof(Vector4)) return Vector4.Lerp((Vector4)a, (Vector4)b, t);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return List(type, (IList)a, (IList)b, t, current as IList);
            if (IsBlendClass(type))
            {
                object target = current != null && current != a && current != b ? current : Activator.CreateInstance(type);
                Blend(type, a, b, t, target);
                return target;
            }
            return t < .5f ? a : b;
        }

        private static IList List(Type type, IList a, IList b, float t, IList current)
        {
            Type element = type.GetGenericArguments()[0];
            bool paired = a.Count == b.Count;
            IList source = t < .5f ? a : b;
            int count = paired ? a.Count : source.Count;
            IList target = current != null && current != a && current != b ? current : (IList)Activator.CreateInstance(type);
            while (target.Count > count) target.RemoveAt(target.Count - 1);
            for (int i = 0; i < count; i++)
            {
                object existing = i < target.Count ? target[i] : null;
                object value = paired ? Value(element, a[i], b[i], t, existing) : Value(element, source[i], source[i], 0, existing);
                if (i < target.Count) target[i] = value; else target.Add(value);
            }
            return target;
        }
    }

    // Ключ суток: полный вид кадра в свой час.
    [Serializable]
    public sealed class LocationFrameKey
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Ключ";
        public float Hour = 13;
        public LocationPostEffects Post = new LocationPostEffects();
    }

    // Ключи суток неба: включены — кадр между соседними ключами смешивается
    // по часу (по кругу через полночь); основные настройки тогда — запас.
    [Serializable]
    public sealed class LocationFrameKeys
    {
        public bool Enabled;
        public List<LocationFrameKey> Keys = new List<LocationFrameKey>();

        public bool Active => Enabled && Keys != null && Keys.Exists(key => key?.Post != null);

        public LocationFrameKey Find(string id) => Keys?.Find(key => key != null && key.Id == id);

        // Вид в этот час (target — куда писать); ключей нет — null.
        public LocationPostEffects AtHour(float hour, LocationPostEffects target)
        {
            List<LocationFrameKey> keys = Keys?.Where(key => key?.Post != null).OrderBy(key => Mathf.Repeat(key.Hour, 24)).ToList();
            if (keys == null || keys.Count == 0) return null;
            if (keys.Count == 1) return LocationFrameBlend.Copy(keys[0].Post, target);
            float h = Mathf.Repeat(hour, 24);
            int next = keys.FindIndex(key => Mathf.Repeat(key.Hour, 24) > h);
            if (next < 0) next = 0;
            int previous = (next - 1 + keys.Count) % keys.Count;
            float from = Mathf.Repeat(keys[previous].Hour, 24), to = Mathf.Repeat(keys[next].Hour, 24);
            float span = Mathf.Repeat(to - from, 24);
            if (span < 1e-4f) span = 24;
            float t = Mathf.Clamp01(Mathf.Repeat(h - from, 24) / span);
            t = t * t * (3 - 2 * t);
            return LocationFrameBlend.Lerp(keys[previous].Post, keys[next].Post, t, target);
        }

        public LocationFrameKeys Clone()
        {
            LocationFrameKeys copy = new LocationFrameKeys { Enabled = Enabled };
            foreach (LocationFrameKey key in Keys ?? new List<LocationFrameKey>())
                if (key != null) copy.Keys.Add(new LocationFrameKey { Id = key.Id, Name = key.Name, Hour = key.Hour, Post = key.Post?.Clone() });
            return copy;
        }
    }

    // Зона места: свой вид кадра в области рисунка места (пиксели, Y вниз),
    // к краю — плавный переход шириной Feather. Действует по точке, на
    // которую смотрит камера (центр кадра).
    [Serializable]
    public sealed class LocationFrameZone
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Зона";
        public bool Enabled = true;
        public bool Ellipse = true;
        public Vector2 Center = new Vector2(960, 540);
        public Vector2 Size = new Vector2(600, 400);
        public float Feather = 250;
        public float Strength = 1;
        public LocationPostEffects Post = new LocationPostEffects();

        public float Weight(Vector2 pixel)
        {
            if (!Enabled || Post == null) return 0;
            Vector2 half = new Vector2(Mathf.Max(1, Size.x / 2), Mathf.Max(1, Size.y / 2));
            Vector2 d = pixel - Center;
            float outside;
            if (Ellipse)
            {
                float r = new Vector2(d.x / half.x, d.y / half.y).magnitude;
                outside = (r - 1) * Mathf.Min(half.x, half.y);
            }
            else outside = Mathf.Max(Mathf.Abs(d.x) - half.x, Mathf.Abs(d.y) - half.y);
            if (outside <= 0) return Mathf.Clamp01(Strength);
            float s = Mathf.Clamp01(outside / Mathf.Max(1, Feather));
            return Mathf.Clamp01(Strength) * (1 - s * s * (3 - 2 * s));
        }
    }
}
