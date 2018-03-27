// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace UnityEditor.ShortcutManagement
{
    // TODO: Find better name
    enum ShortcutState
    {
        Begin = 1,
        End
    }

    struct ShortcutArguments
    {
        public EditorWindow context;
        public ShortcutState state;
    }

    enum ShortcutType
    {
        Action,
        Clutch
    }

    [Serializable]
    struct Identifier
    {
        public string path;

        public Identifier(MethodInfo methodInfo, ShortcutAttribute attribute)
        {
            path = attribute.identifier;
        }

        public override string ToString()
        {
            return path;
        }
    }

    class ShortcutEntry
    {
        readonly Identifier m_Identifier;

        readonly List<KeyCombination> m_DefaultCombinations = new List<KeyCombination>();
        public readonly KeyCombination? prefKeyMigratedValue;
        List<KeyCombination> m_OverridenCombinations;

        readonly Action<ShortcutArguments> m_Action;
        readonly Type m_Context;
        readonly ShortcutType m_Type;

        static readonly object[] k_ReusableShortcutArgs = {null};
        static readonly object[] k_EmptyReusableShortcutArgs = {};

        public Identifier identifier => m_Identifier;

        public IEnumerable<KeyCombination> combinations => activeCombination;

        public bool overridden => m_OverridenCombinations != null;

        public Action<ShortcutArguments> action => m_Action;
        public Type context => m_Context;
        public ShortcutType type => m_Type;

        internal ShortcutEntry(Identifier id, List<KeyCombination> defaultCombination, Action<ShortcutArguments> action, Type context, ShortcutType type, KeyCombination? prefKeyMigratedValue = null)
        {
            m_Identifier = id;
            m_DefaultCombinations = new List<KeyCombination>(defaultCombination);
            m_Context = context;
            m_Action = action;
            m_Type = type;
            this.prefKeyMigratedValue = prefKeyMigratedValue;
        }

        internal static ShortcutEntry CreateFromAttribute(MethodInfo methodInfo, ShortcutAttribute attribute)
        {
            var keyEvent = Event.KeyboardEvent(attribute.defaultKeyCombination);
            var defaultCombination = new List<KeyCombination>();
            var keyCombination = new KeyCombination(keyEvent);
            defaultCombination.Add(keyCombination);
            var identifier = new Identifier(methodInfo, attribute);
            var type = attribute is ClutchShortcutAttribute ? ShortcutType.Clutch : ShortcutType.Action;
            var methodParams = methodInfo.GetParameters();
            Action<ShortcutArguments> action;
            if (methodParams.Length == 0)
            {
                action = shortcutArgs =>
                    {
                        methodInfo.Invoke(null, k_EmptyReusableShortcutArgs);
                    };
            }
            else
            {
                action = shortcutArgs =>
                    {
                        k_ReusableShortcutArgs[0] = shortcutArgs;
                        methodInfo.Invoke(null, k_ReusableShortcutArgs);
                    };
            }

            KeyCombination? prefKeyMigratedValue = null;
            var prefKeyAttr = methodInfo.GetCustomAttributes(
                    typeof(FormerlyPrefKeyAsAttribute), false
                    ).FirstOrDefault() as FormerlyPrefKeyAsAttribute;
            if (prefKeyAttr != null)
            {
                var prefKeyDefaultValue = new KeyCombination(Event.KeyboardEvent(prefKeyAttr.defaultValue));
                string name;
                Event keyboardEvent;
                if (
                    PrefKey.TryParseUniquePrefString(EditorPrefs.GetString(prefKeyAttr.name, prefKeyAttr.defaultValue), out name, out keyboardEvent)
                    )
                {
                    var prefKeyCurrentValue = new KeyCombination(keyboardEvent);
                    if (!prefKeyCurrentValue.Equals(prefKeyDefaultValue))
                        prefKeyMigratedValue = prefKeyCurrentValue;
                }
            }

            return new ShortcutEntry(identifier, defaultCombination, action, attribute.context, type, prefKeyMigratedValue);
        }

        public override string ToString()
        {
            return $"{string.Join(",", combinations.Select(c=>c.ToString()).ToArray())} [{context?.Name}]";
        }

        List<KeyCombination> activeCombination
        {
            get
            {
                if (m_OverridenCombinations != null)
                    return m_OverridenCombinations;
                return m_DefaultCombinations;
            }
        }

        public bool StartsWith(List<KeyCombination> prefix)
        {
            if (activeCombination.Count < prefix.Count)
                return false;

            for (var i = 0; i < prefix.Count; i++)
            {
                if (!prefix[i].Equals(activeCombination[i]))
                    return false;
            }

            return true;
        }

        public bool FullyMatches(List<KeyCombination> other)
        {
            if (activeCombination.Count != other.Count)
                return false;

            for (var i = 0; i < activeCombination.Count; i++)
            {
                if (!activeCombination[i].Equals(other[i]))
                    return false;
            }

            return true;
        }

        public void ResetToDefault()
        {
            m_OverridenCombinations = null;
        }

        public void SetOverride(List<KeyCombination> newKeyCombinations)
        {
            m_OverridenCombinations = new List<KeyCombination>(newKeyCombinations);
        }

        public void ApplyOverride(SerializableShortcutEntry shortcutOverride)
        {
            SetOverride(shortcutOverride.keyCombination);
        }
    }

    [Flags]
    enum ShortcutModifiers
    {
        None = 0,
        Alt = 1,
        ControlOrCommand = 2,
        Shift = 4
    }

    [Serializable]
    struct KeyCombination
    {
        [SerializeField]
        KeyCode m_KeyCode;
        [SerializeField]
        ShortcutModifiers m_Modifiers;

        public KeyCode keyCode => m_KeyCode;
        public ShortcutModifiers modifiers => m_Modifiers;

        public KeyCombination(KeyCode keyCode, ShortcutModifiers shortcutModifiers = ShortcutModifiers.None)
        {
            m_KeyCode = keyCode;
            m_Modifiers = shortcutModifiers;
        }

        internal KeyCombination(Event evt)
        {
            m_KeyCode = evt.keyCode;
            m_Modifiers = ShortcutModifiers.None;

            if (evt.alt)
                m_Modifiers |= ShortcutModifiers.Alt;
            if (evt.control || evt.command)
                m_Modifiers |= ShortcutModifiers.ControlOrCommand;
            if (evt.shift)
                m_Modifiers |= ShortcutModifiers.Shift;
        }

        public bool alt => (modifiers & ShortcutModifiers.Alt) == ShortcutModifiers.Alt;
        public bool controlOrCommand => (modifiers & ShortcutModifiers.ControlOrCommand) == ShortcutModifiers.ControlOrCommand;
        public bool shift => (modifiers & ShortcutModifiers.Shift) == ShortcutModifiers.Shift;

        public Event ToKeyboardEvent()
        {
            Event e = new Event();
            e.type = EventType.KeyDown;
            e.alt = alt;
            e.command = controlOrCommand && Application.platform == RuntimePlatform.OSXEditor;
            e.control = controlOrCommand && Application.platform != RuntimePlatform.OSXEditor;
            e.shift = shift;
            e.keyCode = keyCode;
            return e;
        }

        public static string SequenceToString(IEnumerable<KeyCombination> keyCombinations)
        {
            if (!keyCombinations.Any())
                return "";

            var builder = new StringBuilder();

            builder.Append(keyCombinations.First());

            foreach (var keyCombination in keyCombinations.Skip(1))
            {
                builder.Append(", ");
                builder.Append(keyCombination);
            }

            return builder.ToString();
        }

        public override string ToString()
        {
            return $"{VisualizeModifiers(modifiers)}{VisualizeKeyCode(keyCode)}";
        }

        static string VisualizeModifiers(ShortcutModifiers modifiers)
        {
            var builder = new StringBuilder();

            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                if ((modifiers & ShortcutModifiers.Alt) != 0)
                    builder.Append("⌥");
                if ((modifiers & ShortcutModifiers.Shift) != 0)
                    builder.Append("⇧");
                if ((modifiers & ShortcutModifiers.ControlOrCommand) != 0)
                    builder.Append("⌘");
            }
            else
            {
                if ((modifiers & ShortcutModifiers.ControlOrCommand) != 0)
                    builder.Append("Ctrl+");
                if ((modifiers & ShortcutModifiers.Alt) != 0)
                    builder.Append("Alt+");
                if ((modifiers & ShortcutModifiers.Shift) != 0)
                    builder.Append("Shift+");
            }

            return builder.ToString();
        }

        static string VisualizeKeyCode(KeyCode keyCode)
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                switch (keyCode)
                {
                    case KeyCode.Return: return "↩";
                    case KeyCode.Backspace: return "⌫";
                    case KeyCode.Delete: return "⌦";
                    case KeyCode.Escape: return "⎋";
                    case KeyCode.RightArrow: return "→";
                    case KeyCode.LeftArrow: return "←";
                    case KeyCode.UpArrow: return "↑";
                    case KeyCode.DownArrow: return "↓";
                    case KeyCode.PageUp: return "⇞";
                    case KeyCode.PageDown: return "⇟";
                    case KeyCode.Home: return "↖";
                    case KeyCode.End: return "↘";
                    case KeyCode.Tab: return "⇥";
                }
            }

            return keyCode.ToString();
        }
    }
}