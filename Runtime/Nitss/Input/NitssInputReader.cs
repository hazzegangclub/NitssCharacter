using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Centraliza a leitura de input para teclado e controle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NitssInputReader : MonoBehaviour
    {
        [Header("Enable/Disable")]
        [SerializeField, Tooltip("Quando falso, o personagem não lê nenhum input e permanece parado (útil para dummy/AI).")]
        private bool enableInput = true;
        [SerializeField, Tooltip("Se verdadeiro, desabilita automaticamente o input quando este GameObject tem a tag 'Enemy'.")]
        private bool autoDisableWhenTagIsEnemy = true;
        [Header("Legacy Input Manager")]
        [SerializeField] private string legacyHorizontal = "Horizontal";
        [SerializeField] private string legacyVertical = "Vertical";
        [SerializeField] private string legacyAttackButton = "Fire1";
        [SerializeField] private string legacyHeavyAttackButton = "Fire2";
        [SerializeField] private string legacyBlockButton = "Block";
        [SerializeField] private string legacyJumpButton = "Jump";
        [SerializeField] private string legacyDashButton = "Fire3";
        [SerializeField, Tooltip("Índice do botão de pulo no joystick (KeyCode.JoystickButtonX). No seu mapeamento: Cross = JoystickButton1, Circle = 2, Triangle = 3, Square = 0.")]
        private int legacyGamepadJumpButtonIndex = 1; // Cross = JoystickButton1 conforme instrução
        [SerializeField, Tooltip("Quando um joystick estiver conectado, ignora o mapeamento Legacy 'Jump' e usa apenas o índice acima para pulo.")]
        private bool ignoreLegacyJumpButtonWhenGamepadPresent = true;

        [Header("Keyboard Binds")]
        [SerializeField] private KeyCode keyboardAttackKey = KeyCode.J;
        [SerializeField] private KeyCode keyboardHeavyAttackKey = KeyCode.K;
        [SerializeField] private KeyCode keyboardBlockKey = KeyCode.LeftControl;
        [SerializeField] private KeyCode keyboardJumpKey = KeyCode.Space;
        [SerializeField] private KeyCode keyboardDashKey = KeyCode.LeftShift;

        [Header("Analog Thresholds")]
        [SerializeField, Range(0.05f, 0.95f)] private float triggerThreshold = 0.5f;
        [SerializeField, Tooltip("Limiar para considerar o L2 como pressionado (histerese).")]
        [Range(0f, 1f)] private float blockPressThreshold = 0.35f;
        [SerializeField, Tooltip("Limiar para considerar o L2 como liberado (histerese). Deve ser menor que o de press.")]
        [Range(0f, 1f)] private float blockReleaseThreshold = 0.25f;

        public readonly struct Snapshot
        {
            public Snapshot(Vector2 move, bool block, bool attack, bool heavyAttack, bool jump, bool dash)
            {
                Move = move;
                BlockHeld = block;
                AttackHeld = attack;
                HeavyAttackHeld = heavyAttack;
                JumpHeld = jump;
                DashHeld = dash;
            }

            public Vector2 Move { get; }
            public bool BlockHeld { get; }
            public bool AttackHeld { get; }
            public bool HeavyAttackHeld { get; }
            public bool JumpHeld { get; }
            public bool DashHeld { get; }
        }

        private Snapshot previous;
        private Snapshot current;
        // Latch de histerese + debounce para o Block (evita piscar quando o gatilho oscila)
        private bool blockLatched;
        private float blockDebounceTimer;
        [SerializeField, Tooltip("Tempo mínimo mantendo o estado atual do Block antes de permitir alternância (s)")]
        private float blockDebounceSeconds = 0.05f;

        public Snapshot Current => current;
        public Snapshot Previous => previous;

        public bool AttackPressed => current.AttackHeld && !previous.AttackHeld;
        public bool HeavyAttackPressed => current.HeavyAttackHeld && !previous.HeavyAttackHeld;
        public bool JumpPressed => current.JumpHeld && !previous.JumpHeld;
        public bool DashPressed => current.DashHeld && !previous.DashHeld;

        private void Awake()
        {
            if (autoDisableWhenTagIsEnemy && gameObject.CompareTag("Enemy"))
            {
                enableInput = false;
            }
        }

        public void Sample()
        {
            if (!enableInput)
            {
                previous = current;
                current = new Snapshot(Vector2.zero, false, false, false, false, false);
                return;
            }
            previous = current;
            Vector2 move = ReadMoveVector();
            bool block = ReadBlockHeld();
            bool attack = ReadAttackHeld();
            bool heavy = ReadHeavyAttackHeld();
            bool jump = ReadJumpHeld();
            bool dash = ReadDashHeld();
            current = new Snapshot(move, block, attack, heavy, jump, dash);
        }

        private Vector2 ReadMoveVector()
        {
            Vector2 move = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                move = Gamepad.current.leftStick.ReadValue();
            }
            if (move.sqrMagnitude < 0.0001f && Keyboard.current != null)
            {
                float x = 0f;
                if (Keyboard.current.aKey.isPressed) x -= 1f;
                if (Keyboard.current.dKey.isPressed) x += 1f;
                if (Keyboard.current.leftArrowKey.isPressed) x -= 1f;
                if (Keyboard.current.rightArrowKey.isPressed) x += 1f;
                float y = 0f;
                if (Keyboard.current.sKey.isPressed) y -= 1f;
                if (Keyboard.current.wKey.isPressed) y += 1f;
                if (Keyboard.current.downArrowKey.isPressed) y -= 1f;
                if (Keyboard.current.upArrowKey.isPressed) y += 1f;
                move = new Vector2(x, y);
            }
#endif
            if (move.sqrMagnitude < 0.0001f)
            {
                try
                {
                    move.x = Input.GetAxisRaw(legacyHorizontal);
                    move.y = Input.GetAxisRaw(legacyVertical);
                }
                catch { }
            }
            return Vector2.ClampMagnitude(move, 1f);
        }

        private bool ReadBlockHeld()
        {
            bool held = false;
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                // Requisito atualizado: bloquear enquanto L2 (gatilho esquerdo) estiver pressionado
                float v = Gamepad.current.leftTrigger.ReadValue(); // 0..1
                // Histerese: só libera quando cair abaixo de um limiar menor
                if (blockDebounceTimer > 0f)
                {
                    blockDebounceTimer = Mathf.Max(0f, blockDebounceTimer - Time.unscaledDeltaTime);
                }
                bool next;
                if (!blockLatched)
                    next = v >= Mathf.Min(1f, Mathf.Max(blockPressThreshold, 0f));
                else
                    next = v >= Mathf.Min(1f, Mathf.Max(blockReleaseThreshold, 0f));
                if (next != blockLatched && blockDebounceTimer <= 0f)
                {
                    blockLatched = next;
                    blockDebounceTimer = blockDebounceSeconds;
                }
                held |= blockLatched;
            }
            if (Keyboard.current != null)
            {
                held |= Keyboard.current.leftCtrlKey.isPressed;
                held |= Keyboard.current.rightCtrlKey.isPressed;
            }
#endif
            if (!held)
            {
                if (keyboardBlockKey != KeyCode.None && Input.GetKey(keyboardBlockKey)) held = true;
                try { held |= Input.GetButton(legacyBlockButton); } catch { }
            }
            return held;
        }

        private bool ReadAttackHeld()
        {
            bool held = false;
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                held |= Gamepad.current.buttonWest.isPressed;
            }
            if (Keyboard.current != null)
            {
                held |= Keyboard.current.jKey.isPressed;
            }
#endif
            if (!held)
            {
                if (keyboardAttackKey != KeyCode.None && Input.GetKey(keyboardAttackKey)) held = true;
                try { held |= Input.GetButton(legacyAttackButton); } catch { }
            }
            return held;
        }

        private bool ReadHeavyAttackHeld()
        {
            bool held = false;
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                held |= Gamepad.current.buttonNorth.isPressed;
            }
            if (Keyboard.current != null)
            {
                held |= Keyboard.current.kKey.isPressed;
            }
#endif
            if (!held)
            {
                if (keyboardHeavyAttackKey != KeyCode.None && Input.GetKey(keyboardHeavyAttackKey)) held = true;
                try { held |= Input.GetButton(legacyHeavyAttackButton); } catch { }
            }
            return held;
        }

        private bool ReadJumpHeld()
        {
            bool held = false;
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                held |= Gamepad.current.buttonSouth.isPressed;
            }
            if (Keyboard.current != null)
            {
                held |= Keyboard.current.spaceKey.isPressed;
            }
#endif
            if (!held)
            {
                if (keyboardJumpKey != KeyCode.None && Input.GetKey(keyboardJumpKey)) held = true;
                // Verifica joysticks conectados no modo Legacy
                bool gamepadPresentLegacy = false;
                try { var names = Input.GetJoystickNames(); gamepadPresentLegacy = names != null && names.Length > 0; } catch { }

                // Se houver joystick e a flag estiver ativa, ignorar o eixo/botão Legacy "Jump" (evita pegar Square acidentalmente)
                if (!(gamepadPresentLegacy && ignoreLegacyJumpButtonWhenGamepadPresent))
                {
                    try { held |= Input.GetButton(legacyJumpButton); } catch { }
                }

                // Checagem explícita por índice de botão do controle no modo Legacy
                if (legacyGamepadJumpButtonIndex >= 0 && legacyGamepadJumpButtonIndex <= 19)
                {
                    var code = (KeyCode)((int)KeyCode.JoystickButton0 + legacyGamepadJumpButtonIndex);
                    if (Input.GetKey(code)) held = true;
                }
            }
            return held;
        }

        private bool ReadDashHeld()
        {
            bool held = false;
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                // Dash agora usa R2 (gatilho direito) + botão East como alternativa
                held |= Gamepad.current.rightTrigger.ReadValue() > triggerThreshold;
                held |= Gamepad.current.buttonEast.isPressed;
            }
            if (Keyboard.current != null)
            {
                held |= Keyboard.current.leftShiftKey.isPressed;
                held |= Keyboard.current.rightShiftKey.isPressed;
            }
#endif
            if (!held)
            {
                if (keyboardDashKey != KeyCode.None && Input.GetKey(keyboardDashKey)) held = true;
                try { held |= Input.GetButton(legacyDashButton); } catch { }
            }
            return held;
        }
    }
}
