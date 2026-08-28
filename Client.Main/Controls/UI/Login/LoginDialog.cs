using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace Client.Main.Controls.UI.Login
{
    public class LoginDialog : PopupFieldDialog
    {
        // Fields
        private readonly TextureControl _line1;
        private readonly TextureControl _line2;
        private readonly TextFieldControl _userInput;
        private readonly LabelControl _serverNameLabel;
        private readonly TextFieldControl _passwordInput;
        private readonly OkButton _okButton;

        // 送出登入後到伺服器回應之間約 1-2 秒沒有任何回饋，實測使用者會以為沒反應
        // 而重複點擊 —— 第一次其實已經登入成功，第二次就收到 AccountAlreadyConnected。
        // 這裡在等待期間鎖住按鈕並給視覺提示。
        private bool _isSubmitting;
        private double _submitElapsedSeconds;

        /// <summary>逾時保護。封包遺失或伺服器無回應時，避免按鈕永久鎖死。</summary>
        private const double SubmitTimeoutSeconds = 15.0;

        // Properties
        public string ServerName
        {
            get => _serverNameLabel.Text;
            set => _serverNameLabel.Text = value;
        }

        /// <summary>
        /// Gets the username entered in the text field.
        /// </summary>
        public string Username => _userInput.Value;

        /// <summary>
        /// Gets the password entered in the text field.
        /// </summary>
        public string Password => _passwordInput.Value;

        // Events
        /// <summary>
        /// Invoked when the user confirms login (clicks OK or presses Enter in the password field).
        /// </summary>
        public event EventHandler LoginAttempt;

        // Constructors
        public LoginDialog()
        {
            ControlSize = new Point(300, 200);

            Controls.Add(new LabelControl
            {
                Text = "MU Online",
                Align = ControlAlign.HorizontalCenter,
                Y = 15,
                FontSize = 12
            });

            Controls.Add(_line1 = new TextureControl
            {
                TexturePath = "Interface/GFx/popup_line_m.ozd",
                X = 10,
                Y = 40,
                AutoViewSize = false
            });

            Controls.Add(_serverNameLabel = new LabelControl
            {
                Text = "OpenMU Server 1",
                Align = ControlAlign.HorizontalCenter,
                Y = 55,
                FontSize = 12,
                TextColor = new Color(241, 188, 37)
            });

            Controls.Add(new LabelControl
            {
                Text = "User",
                Y = 90,
                X = 20,
                AutoViewSize = false,
                ViewSize = new Point(70, 20),
                TextAlign = HorizontalAlign.Right,
                FontSize = 12f
            });

            Controls.Add(new LabelControl
            {
                Text = "Password",
                Y = 120,
                X = 20,
                AutoViewSize = false,
                ViewSize = new Point(70, 20),
                TextAlign = HorizontalAlign.Right,
                FontSize = 12f
            });

            Controls.Add(_line2 = new TextureControl
            {
                TexturePath = "Interface/GFx/popup_line_m.ozd",
                X = 10,
                Y = 150,
                AutoViewSize = false,
                Alpha = 0.7f
            });

            _userInput = TextFieldControl.Create();
            _userInput.X = 100;
            _userInput.Y = 87;
            _userInput.Skin = TextFieldSkin.NineSlice;

            _passwordInput = TextFieldControl.Create();
            _passwordInput.X = 100;
            _passwordInput.Y = 117;
            _passwordInput.MaskValue = true;
            _passwordInput.Skin = TextFieldSkin.NineSlice;

            _passwordInput.ValueChanged += PasswordInput_EnterPressed; // Use dedicated method
            Controls.Add(_userInput);
            Controls.Add(_passwordInput);

            _userInput.Click += (s, e) => { _userInput.OnFocus(); _passwordInput.OnBlur(); };
            _passwordInput.Click += (s, e) => { _passwordInput.OnFocus(); _userInput.OnBlur(); };

            _okButton = new OkButton
            {
                Y = 160,
                Align = ControlAlign.HorizontalCenter
            };
            _okButton.Click += OkButton_Click; // Use dedicated method
            Controls.Add(_okButton);

            // 帶入上次登入的帳號與密碼。手機上每次都要叫出系統鍵盤重打非常麻煩。
            // 密碼的保存方式與安全性取捨見 MuGame.PersistLoginCredentials。
            var lastUser = MuGame.LoadLastUsername();
            if (!string.IsNullOrEmpty(lastUser))
            {
                _userInput.Value = lastUser;
            }
            var lastPassword = MuGame.LoadLastPassword();
            if (!string.IsNullOrEmpty(lastPassword))
            {
                _passwordInput.Value = lastPassword;
            }
        }

        // Public Methods
        /// <summary>
        /// Sets focus on the username field (called from the scene).
        /// </summary>
        public void FocusUsername()
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                _userInput?.OnFocus();
                _passwordInput?.OnBlur();
            });
        }

        private void SetSubmitting(bool submitting)
        {
            _isSubmitting = submitting;
            _submitElapsedSeconds = 0;

            if (_okButton != null)
            {
                _okButton.Interactive = !submitting;
                _okButton.Alpha = submitting ? 0.45f : 1f;
            }
        }

        /// <summary>
        /// 由 LoginScene 在登入失敗、連線錯誤或狀態回復時呼叫，解除送出鎖定。
        /// </summary>
        public void ResetSubmitState() => SetSubmitting(false);

        public override void Update(GameTime gameTime)
        {
            // 逾時保護：伺服器沒有回應時解除鎖定，否則按鈕會永久按不下去
            if (_isSubmitting)
            {
                _submitElapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;
                if (_submitElapsedSeconds >= SubmitTimeoutSeconds)
                {
                    SetSubmitting(false);
                }
            }

            // Handle Tab key to switch focus between input fields
            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Tab) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Tab))
            {
                if (_userInput.IsFocused)
                {
                    _userInput.OnBlur();
                    _passwordInput.OnFocus();
                }
                else if (_passwordInput.IsFocused)
                {
                    _passwordInput.OnBlur();
                    _userInput.OnFocus();
                }
            }
            base.Update(gameTime);
        }

        // Protected Methods
        protected override void OnScreenSizeChanged()
        {
            _line1.ViewSize = new Point(DisplaySize.X - 20, 8);
            _line2.ViewSize = new Point(DisplaySize.X - 20, 5);
            base.OnScreenSizeChanged();
        }

        // Private Methods
        // Method called after clicking the OK button
        private void OkButton_Click(object sender, EventArgs e)
        {
            AttemptLogin();
        }

        // Method called after pressing Enter in the password field
        private void PasswordInput_EnterPressed(object sender, EventArgs e)
        {
            // ValueChanged is also invoked on text change,
            // so we check if Enter was just pressed.
            bool enterPressed = MuGame.Instance.Keyboard.IsKeyDown(Keys.Enter) &&
                                MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Enter);

            if (enterPressed)
            {
                AttemptLogin();
            }
        }

        // Invokes the LoginAttempt event
        private void AttemptLogin()
        {
#if IOS
            Console.WriteLine($"[MuIos.Login] AttemptLogin user='{_userInput.Value}' passwordLength={_passwordInput.Value?.Length ?? 0}");
#endif
            if (_isSubmitting)
            {
                return;
            }

            SetSubmitting(true);
            MuGame.PersistLoginCredentials(_userInput.Value, _passwordInput.Value);
            // Blur fields to hide soft keyboard (especially on mobile) after submitting.
            _userInput.OnBlur();
            _passwordInput.OnBlur();
            if (Scene != null && (Scene.FocusControl == _userInput || Scene.FocusControl == _passwordInput))
            {
                Scene.FocusControl = null; // keep focus cleared so keyboard stays hidden
            }

            LoginAttempt?.Invoke(this, EventArgs.Empty);
        }
    }
}
