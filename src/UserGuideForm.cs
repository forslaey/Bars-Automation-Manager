using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class GuideSection
    {
        internal readonly string Title, Body;
        internal GuideSection(string title, string body) { Title = title; Body = body; }
        internal string Document { get { return Title + "\n\n" + Body.Replace("\r\n", "\n"); } }
        public override string ToString() { return Title; }
    }

    internal static class UserGuide
    {
        internal static readonly GuideSection[] Sections = {
            new GuideSection("Начало работы",
                "Запусти менеджер и выбери карточку нужного модуля. Перед действием проверь выбранный файл, папку или строки. Если Windows запросит права администратора, проверь действие и подтверди запрос. Итог и ошибки смотри в журнале модуля.\n\n" +
                "«В меню» возвращает к главному окну. «Закрыть» закрывает текущий модуль, «Закрыть программу» — весь менеджер. Повторный выбор карточки открывает уже запущенный модуль.\n\n" +
                "Как найти нужный текст\nНажми Ctrl+F и введи слово в «Поиск по всей инструкции». «Далее» и «Назад» переключают совпадения. Можно использовать F3 и Shift+F3. «Сбросить» возвращает все разделы. Инструкция доступна без интернета.\n\n" +
                "Как обновить сам менеджер\nОткрой «О программе» → «Проверить обновления» или дождись уведомления о новой версии. Скачивание можно отменить. После подтверждения менеджер проверит файл, закроется и запустит новую версию. Если выполняется другая операция, дождись её окончания. Если автоматическое обновление недоступно, скачай новый EXE на GitHub вручную."),
            new GuideSection("Найти комплект и выбрать папку",
                "Комплект — папка с файлами для модулей. Стандартный путь: C:\\Менеджер автоматизации Барс. Название папки может быть любым.\n\n" +
                "При открытии модуля менеджер в фоне проверяет сохранённый путь, папки рядом с EXE и ближайшие каталоги на локальных дисках и флешках. Неполный комплект подходит, если в нём есть файлы для нужного модуля.\n\n" +
                "Если подходит несколько папок, откроется окно «Найденные комплекты». В таблице видны путь и разделы каждой папки. Выдели нужную строку и нажми «Выбрать комплект». Длинный путь можно прочитать при наведении. Найденный путь используется в текущем сеансе. Если папка не найдена, укажи её в «Скачать комплект» или в модуле, где есть выбор пути.\n\n" +
                "Выбор пути и создание папки\nДвойной щелчок или двойное касание открывает папку. «Перейти» открывает выделенную папку или введённый путь. «Выше» поднимает на уровень, боковые кнопки мыши возвращают назад или вперёд, свайп прокручивает список. Для создания подпапки нажми «Новая папка», введи имя, проверь диск и путь и нажми «Создать». Длинный путь можно прочитать целиком при наведении.\n\n" +
                "Если менеджер просит .NET Framework 4.8\nЗапускатель ищет установщик в _barsmanager. Можно указать папку с установщиком или скачать его через Яндекс Диск, Google Диск либо сайт Microsoft. Современные .NET Runtime и ASP.NET Core Runtime ставятся отдельно через менеджер библиотек."),
            new GuideSection("Скачать комплект",
                "Открой «Скачать комплект» внизу главного окна. Через это окно файлы сохраняются на диск. Для установки затем открой нужный модуль.\n\n" +
                "1. Укажи папку назначения и отметь нужные разделы. По умолчанию галочки сняты. Для SQL Server и SSMS также выбери нужные наборы.\n2. Нажми «Проверить» и выбери облако. В результате проверки отметь нужные файлы и нажми «Применить».\n3. Нажми «Скачать» и выбери источник загрузки. Дождись итога в журнале.\n\n" +
                "«Результаты» открывает последнюю сверку. «Перепроверить файлы» сравнивает локальные копии с уже загруженным каталогом. «Пауза» и «Продолжить» управляют загрузкой, «Отменить» останавливает очередь. Уже проверенные и сохранённые файлы остаются в папке. Если источник недоступен, менеджер может предложить другое облако.\n\n" +
                "Процент показывает ход операции. Ориентируйся на итоговую надпись и журнал. Список программ, служб и агентов Барс скачивается из их собственных карточек."),
            new GuideSection("Менеджер служб",
                "Установить или удалить\n1. Открой «Менеджер служб» и выбери EXE. Его также можно перетащить в окно.\n2. Проверь связанные службы. Нажми «Установить службу» или «Удалить службу». Для совместимой службы можно открыть «Настройка установки» и указать учётную запись Windows.\n3. Прочитай подтверждение и дождись проверки регистрации. Если один EXE зарегистрировал несколько служб, проверь, какие из них затронет действие. При коде 1060 менеджер повторно сверяет путь и может предложить удалить регистрацию службы с другим системным именем. Файлы программы при этом сохраняются.\n\n" +
                "Для HttpService менеджер проверяет путь к DLL расширения в конфиге и при необходимости исправляет его. Для агента АТОЛ при проверке совместимости сверяй разрядность драйвера ККТ и требования к .NET.\n\n" +
                "Запустить, остановить или изменить режим\nВыдели строку службы и нажми «Запустить», «Остановить» или выбери «Режим запуска» и нажми «Применить». Для перезапуска сначала дождись остановки, затем запусти службу. «Обновить статус» читает состояние заново. Можно включить проверку каждые 10 секунд или открыть оснастку кнопкой «Открыть службы».\n\n" +
                "После отмены ожидания команда, уже переданная Windows, может завершиться. Обнови статус и сверь итог в журнале."),
            new GuideSection("Диагностика службы и Swagger",
                "В «Менеджере служб» выдели службу или выбери EXE работающего процесса и нажми «Диагностика». Менеджер ищет адрес процесса. Если адрес найден, нажми «Проверить отклик». Можно включить проверку каждые 10 секунд.\n\n" +
                "Если менеджер не нашёл адрес, проверь журнал и при необходимости укажи известный локальный адрес диагностики. Ответ 401 или 403 означает, что сервер ответил и требует доступа. При тайм-ауте перепроверь адрес и журналы службы. Успешный отклик подтверждает доступность выбранного адреса.\n\n" +
                "«Открыть Swagger» появляется, если найдена страница описания API. Перед использованием «Try it out» прочитай назначение метода и его параметры. Открытие Swagger само по себе не проверяет платежи, оборудование или базу данных."),
            new GuideSection("Собрать логи программы или службы",
                "Для программы Барс открой карточку «Логи программ». Выбери найденную программу или нажми «Выбрать папку…» и укажи каталог программы, внутри которого лежит Logs. Папку можно перетащить в окно. При следующем выборе откроется последняя подтверждённая папка.\n\n" +
                "Для службы открой «Менеджер служб», выдели службу и нажми «Собрать логи». Останавливать службу для сбора не нужно.\n\n" +
                "1. Выбери «Только за сегодня», «Выбранные даты» или «Все логи».\n2. Для выбранных дат отметь нужные папки. Отметки сохраняются при переходе между страницами.\n3. Укажи путь сохранения ZIP. Дождись сообщения о готовом архиве и проверь его содержимое перед отправкой.\n\n" +
                "При ошибке 1064 служба столкнулась с внутренней ошибкой. При 1053 она не ответила вовремя. Проверь её логи за время попытки и, если нужных записей нет, журнал «Приложение» в Windows."),
            new GuideSection("Менеджер библиотек",
                "Открой «Менеджер библиотек» и нажми «Сканировать систему». Сравни версии в столбцах «Комплект» и «В системе». Если комплект не найден, нажми «Выбрать папку…» и укажи корень или _libraries.\n\n" +
                "«Выбрать недостающие» отмечает подходящие отсутствующие компоненты. Укажи нужные строки и проверь очередь перед запуском.\n\n" +
                "• «По рекомендации» устанавливает отсутствующее или обновляет устаревшее.\n• «Переустановить» удаляет выбранный установленный компонент и ставит его заново.\n• «Удалить» запускает поддерживаемую процедуру удаления.\n\n" +
                "Разные поколения и разрядности могут требоваться одновременно. Если Visual C++ поставляется в архиве 7z, положи его в _libraries и обнови список. Для поддерживаемого архива менеджер проверит и запустит установщик сам.\n\n" +
                "После ошибки, отмены или запроса перезагрузки дождись окончания системной операции и повтори проверку. Для подробностей нажми «Открыть журнал»."),
            new GuideSection(".NET и компоненты Windows",
                "Скачать установщики .NET\nВ менеджере библиотек открой «Проверить обновления .NET» и нажми «Проверить версии в сети». Отметь нужные версии и нажми «Обновить выбранные». Менеджер скачает и проверит установщики в папке библиотек. Чтобы установить их в Windows, вернись в «Менеджер библиотек», пересканируй систему и выбери действие.\n\n" +
                "Включить IIS, Telnet или TFTP\nНажми «Компоненты» в менеджере библиотек. Дождись проверки, отметь нужные компоненты и подтверди действие. При запросе перезагрузки сохрани работу и согласуй время. «Назад» возвращает в менеджер библиотек."),
            new GuideSection("Прикладные программы",
                "Поддерживаются Chrome, Notepad++, 7-Zip, AnyDesk и RuDesktop. При открытии менеджер показывает установленную версию и найденный локальный пакет.\n\n" +
                "1. Проверь папку комплекта или _soft.\n2. Нажми «Проверить версии в сети», если нужны свежие сведения.\n3. Проверь отмеченные строки и выбери установку, обновление или удаление. Если нужного установщика нет, менеджер может скачать его при установке и сохранить в _soft.\n\n" +
                "Если версия скачанного Chrome отличается от ожидаемой, менеджер оставляет имеющийся пакет и пишет причину в журнале. Перед удалением прочитай, какие настройки будут очищены. В Notepad++ сначала сохрани открытые документы.\n\n" +
                "«Приложения по умолчанию» открывает настройки Windows для выбора браузера и других программ."),
            new GuideSection("Удалённый доступ AnyDesk и RuDesktop",
                "Для установленной программы нажми «Настроить удалённый доступ». При установке включи настройку доступа перед запуском.\n\n" +
                "Отметь «Стандартный пароль Барса», если этот вариант настроен. Оба поля ввода станут недоступны. Для своего пароля сними отметку и введи от 8 до 128 символов дважды. Значок глаза показывает текст, а РУС или ENG под полем показывает раскладку. Нажми «Продолжить».\n\n" +
                "Если программа сообщает, что её служба не запущена, запусти клиент и проверь службу Windows. Если клиент отклонил пароль, проверь текущий пароль или задай другой. После настройки проверь подключение с другого компьютера."),
            new GuideSection("Программы Барс, службы и агенты",
                "Программы лежат в _soft\\_Bars\\_Soft, архивы служб и агентов — в _soft\\_Bars\\_Services. Вложенные папки читаются автоматически, папка _custom пропускается.\n\n" +
                "Как найти нужную версию\n1. Выбери «Официальный репозиторий» или «Яндекс Диск» над таблицей и нажми «Проверить в сети». Для официального репозитория применяется автоматический вход, если он настроен. «Локальный источник» означает, что файл уже есть на компьютере.\n2. При открытии группы свёрнуты. Свёрнутая группа показывает, сколько пакетов в ней отмечено. Последние выпуски идут сверху. Для выпуска 168 порядок подверсий — 2.168, 1.168, 0.168. В «Программах Барс» раскрой выпуск и полную версию. В «Службах и агентах Барс» раскрой _Services для отдельных архивов вроде HttpService или _bin для общих выпусков.\n3. Отметь нужные строки и нажми «Скачать выбранные». Яндекс Диск сохраняет структуру папок. Перед сохранением проверяются формат и SHA-256.\n\n" +
                "Установка и распаковка\nДля программы нажми «Установить» и выбери родительскую папку. Менеджер создаст подпапку программы, например Administrator. «Ярлык» отмечается отдельно для каждого поддерживаемого установщика. Пакеты меньше 2 МБ нельзя установить.\n\n" +
                "Для архива нажми «Распаковать» и выбери папку назначения. Каждый архив попадёт в свою подпапку. Повреждённый архив распаковать нельзя. Если выбранные пакеты используют одну папку, менеджер покажет их список. Оставь по одному пакету на папку и повтори действие. Службу можно зарегистрировать через «Менеджер служб».\n\n" +
                "Пример размещения при скачивании версии 2.12.2.168 из официального репозитория: _Soft\\2.12.xx.168\\168.2 для программ и _Services\\_bin\\168.2 для архивов общего выпуска. У архивов со своей нумерацией остаётся корень _Services."),
            new GuideSection("SQL Server и SSMS",
                "Открой карточку «SQL Server и SSMS». Менеджер ищет наборы в _soft\\_SQL и показывает шаги установки. Если нужного набора нет, нажми «Скачать наборы…».\n\n" +
                "• Для SQL Server Express сначала запускай основной установщик, затем обновление.\n• Для ISO менеджер подключит образ и откроет setup.exe.\n• Для автономного ZIP с SSMS нажми «Распаковать комплект», затем выбери установщик.\n\n" +
                "Для запуска нажми «Запустить мастер установки». Настройки выбирай в мастере Microsoft. Если установщик не распознан, добавь его в набор через «Подготовить набор…»."),
            new GuideSection("Утилиты и прошивки",
                "В одном окне можно скачать, распаковать или удалить наборы. Поддерживаются папки и архивы ZIP, RAR и 7z.\n\n" +
                "1. Проверь папку комплекта. Чтобы скачать утилиты, выбери Яндекс Диск или Google Диск и нажми «Проверить в сети». Если Google Диск сообщает, что у папки несколько страниц, выбери Яндекс Диск или попроси администратора настроить доступ к Drive API.\n2. После выбора файлов укажи «Путь распаковки». Если выбрана родительская папка, менеджер добавит _tools.\n3. Отметь «Ярлык» у нужных утилит. Если в наборе несколько версий, раскрой строку и отметь версии по отдельности.\n4. Нажми «Распаковать». Повторная распаковка обновит выбранный набор. Соседние наборы сохраняются. При отмене распаковки отметки выбранных наборов сохраняются, пока окно открыто.\n\n" +
                "Если архив повреждён, менеджер укажет его имя. Для поиска исправной копии можно проверить другое облако. Перед удалением проверь список утилит и путь назначения в подтверждении."),
            new GuideSection("Первичные системные настройки",
                "Выбери строку настройки, прочитай её состояние и нажми соответствующую кнопку. Каждая группа применяется отдельно. Кнопка «Проверить настройки» обновляет сведения о Windows. «Вернуть стандартные политики» восстанавливает стандартные значения Windows для выбранной группы.\n\n" +
                "• Брандмауэр. Можно изменить все три профиля: доменный, частный и общедоступный. Достаточно подтвердить права администратора. Менеджер записывает исходное состояние и повторно считывает профили после изменения. Если результат отличается, проверь строку и журнал.\n• UAC. После изменения требуется перезагрузка. Для паролей и SmartScreen после действия проверяются итоговые параметры. Политика организации может переопределить локальную настройку.\n• Права папок. «Проверить папки» читает текущие разрешения. После выдачи прав смотри в журнале, какие объекты не удалось проверить.\n• Режим КСО. После записи настроек проверь жесты на устройстве. «Вернуть прежние настройки» использует сохранённую копию, если она доступна.\n\n" +
                "«Часовой пояс» и «Применить обои» открывают отдельные окна выбора. Для обоев можно выбрать «КСО» или «КАССА». Файлы берутся из _wallpers.")
        };

        internal static List<int> Find(string text, string query)
        {
            var result = new List<int>();
            if (String.IsNullOrWhiteSpace(query)) return result;
            for (int start = 0; start <= text.Length - query.Length; )
            {
                int index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                result.Add(index); start = index + query.Length;
            }
            return result;
        }
    }

    internal sealed class UserGuideForm : ManagerModuleForm
    {
        private readonly BrandTextBox search = new BrandTextBox();
        private readonly GuideSectionListBox sections = new GuideSectionListBox();
        private readonly GuideReader reader = new GuideReader();
        private readonly GuideReaderScrollBar readerScroll=new GuideReaderScrollBar();
        private readonly GuideSectionScrollBar sectionScroll=new GuideSectionScrollBar();
        private readonly Label results = new Label();
        private readonly Label searchCaption;
        private readonly BrandButton previous, next;
        private readonly List<Tuple<GuideSection, int>> hits = new List<Tuple<GuideSection, int>>();
        private int activeHit = -1;
        private bool binding;
        private string Query { get { return search.Text.Trim(); } }

        public UserGuideForm() : base("Инструкция", new Size(1100, 780))
        {
            MinimumSize = new Size(940, 660); StartPosition = FormStartPosition.CenterParent;
            Root.RowStyles[0].Height = 72;
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            var find = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var searchButtons = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            for (int i = 0; i < 3; i++) searchButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            searchCaption = Caption("Поиск по всей инструкции:", false);
            searchCaption.Font = new Font(Font, FontStyle.Bold);
            searchCaption.Dock = DockStyle.None;
            find.Controls.Add(searchCaption);
            search.Dock = DockStyle.None;search.Anchor=AnchorStyles.Left|AnchorStyles.Right; search.AutoSize = true; search.Margin = Padding.Empty; search.AccessibleName = "Поиск по всей инструкции";
            var searchField = new PathFieldFrame(search);
            searchField.Dock = DockStyle.None;
            find.Controls.Add(searchField);
            previous = Button("Назад", false); next = Button("Далее", false); var clear = Button("Сбросить", false); clear.Margin = Padding.Empty;
            previous.Click += delegate { MoveHit(-1); }; next.Click += delegate { MoveHit(1); }; clear.Click += delegate { search.Clear(); search.Focus(); };
            searchButtons.Controls.Add(previous, 0, 0); searchButtons.Controls.Add(next, 1, 0); searchButtons.Controls.Add(clear, 2, 0);
            find.Controls.Add(searchButtons);
            body.Controls.Add(find, 0, 0);
            bool arrangingSearch = false;
            find.Layout += delegate
            {
                int width = find.ClientSize.Width;
                if (arrangingSearch || width <= 0) return;
                arrangingSearch = true;
                try
                {
                    float scale = Root.RowStyles[0].Height / 72F;
                    int captionHeight = (int)Math.Round(24 * scale);
                    int rowHeight = (int)Math.Round(40 * scale);
                    bool narrow = width < (int)Math.Round(560 * scale);
                    int buttonWidth = narrow ? width : (int)Math.Round(330 * scale);
                    int fieldWidth = narrow ? width : width - buttonWidth - (int)Math.Round(10 * scale);
                    int height = captionHeight + rowHeight * (narrow ? 2 : 1);
                    if (body.RowStyles[0].Height != height) body.RowStyles[0].Height = height;
                    searchCaption.SetBounds(0, 0, width, captionHeight);
                    int fieldHeight = Math.Min(rowHeight, search.PreferredHeight + (int)Math.Round(8 * scale));
                    searchField.SetBounds(0, captionHeight + (rowHeight - fieldHeight) / 2, fieldWidth, fieldHeight);
                    searchButtons.SetBounds(width - buttonWidth, captionHeight + (narrow ? rowHeight : 0), buttonWidth, rowHeight);
                }
                finally { arrangingSearch = false; }
            };
            results.Dock = DockStyle.Fill; results.TextAlign = ContentAlignment.MiddleLeft; results.Tag = "muted"; results.Margin = Padding.Empty; results.AccessibleName = "Результаты поиска";
            body.Controls.Add(results, 0, 1);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sections.Dock = DockStyle.Fill; sections.IntegralHeight = false; sections.BorderStyle = BorderStyle.None; sections.Margin = Padding.Empty;
            sections.Font = new Font("Segoe UI", 10F); sections.AccessibleName = "Разделы инструкции";
            sections.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                Color background = selected ? (Colors.Dark ? Colors.Selection : Colors.Accent) : Colors.Canvas;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
                var bounds = sections.TitleBounds(e.Graphics,e.Bounds);
                TextRenderer.DrawText(e.Graphics, sections.Items[e.Index].ToString(), sections.Font, bounds, selected&&!Colors.Dark?Colors.AccentText:Colors.Text,
                    GuideSectionListBox.TitleFlags | TextFormatFlags.VerticalCenter);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
            reader.Dock = DockStyle.Fill; reader.ReadOnly = true; reader.BorderStyle = BorderStyle.None; reader.Margin = Padding.Empty;
            reader.Font = new Font("Segoe UI", 11F); reader.DetectUrls = false; reader.WordWrap = true; reader.HideSelection = true;
            reader.ScrollBars = RichTextBoxScrollBars.None; reader.AccessibleName = "Текст инструкции";
            var readingCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty };
            readerScroll.Dock=DockStyle.Right;readerScroll.Width=14;readerScroll.Bind(reader);readingCard.Controls.Add(reader);readingCard.Controls.Add(readerScroll);
            var sectionSlot=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,0,14,0)};sectionScroll.Dock=DockStyle.Right;sectionScroll.Width=14;sectionScroll.Bind(sections);sectionSlot.Controls.Add(sections);sectionSlot.Controls.Add(sectionScroll);content.Controls.Add(sectionSlot, 0, 0); content.Controls.Add(readingCard, 1, 0); body.Controls.Add(content, 0, 2);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.Controls.Add(Label("Ctrl+F — поиск; F3 / Shift+F3 — переход к совпадению", true), 0, 0);
            var close = Button("Закрыть", false); ModuleNavigation.Configure(close); close.Click += delegate { Close(); }; footer.Controls.Add(close, 1, 0); ModuleNavigation.StandardFooter(footer, close); body.Controls.Add(footer, 0, 3);
            Root.Controls.Add(body, 0, 1);
            search.TextChanged += delegate { Filter(); };
            sections.SelectedIndexChanged += delegate { if (!binding) ShowSection(true); };
            SetTheme(Colors.Dark, false); Filter();
        }

        private void Filter()
        {
            GuideSection selected = sections.SelectedItem as GuideSection;
            binding = true; sections.BeginUpdate(); sections.Items.Clear(); hits.Clear(); activeHit = -1;
            foreach (GuideSection section in UserGuide.Sections)
            {
                List<int> positions = UserGuide.Find(section.Document, Query);
                if (Query.Length == 0 || positions.Count > 0) sections.Items.Add(section);
                foreach (int position in positions) hits.Add(Tuple.Create(section, position));
            }
            if (sections.Items.Count > 0) sections.SelectedItem = selected != null && sections.Items.Contains(selected) ? selected : sections.Items[0];
            sections.EndUpdate();sections.RefreshHeights();binding = false; ShowSection(true);
        }

        private void ShowSection(bool selectFirst)
        {
            reader.BeginFormatting();try{
            GuideSection section = sections.SelectedItem as GuideSection;
            reader.Text = section == null ? "Ничего не найдено. Сократи запрос или нажми «Сбросить»." : section.Document;
            reader.SelectAll(); reader.SelectionColor = Colors.Text; reader.SelectionBackColor = Colors.Surface;
            reader.SelectionFont = reader.Font;
            if (section != null)
            {
                reader.Select(0, section.Title.Length);
                using (var titleFont = new Font(reader.Font.FontFamily, 15F, FontStyle.Bold)) reader.SelectionFont = titleFont;
                foreach (int position in UserGuide.Find(reader.Text, Query))
                { reader.Select(position, Query.Length); reader.SelectionBackColor = Colors.Dark ? Color.FromArgb(100, 85, 24) : Color.FromArgb(255, 235, 150); }
            }
            reader.Select(0, 0);
            if (selectFirst) activeHit = hits.FindIndex(hit => hit.Item1 == section);
            SelectHit();
            }finally{reader.EndFormatting();}
        }

        private void MoveHit(int delta)
        {
            if (hits.Count == 0) return;
            activeHit = (activeHit + delta + hits.Count) % hits.Count;
            binding = true; sections.SelectedItem = hits[activeHit].Item1; binding = false;
            ShowSection(false);
        }

        private void SelectHit()
        {
            previous.Enabled = next.Enabled = hits.Count > 0;
            ((TableLayoutPanel)results.Parent).RowStyles[1].Height=(float)((Query.Length==0?8:24)*Root.RowStyles[0].Height/72F);results.Visible=Query.Length>0;
            results.Text = Query.Length == 0 ? "" : hits.Count == 0 ? "Совпадений нет" :
                "Совпадение " + (activeHit + 1) + " из " + hits.Count + "; разделов: " + sections.Items.Count;
            if (activeHit >= 0 && activeHit < hits.Count)
            { reader.Select(hits[activeHit].Item2, Query.Length); reader.ScrollToCaret();reader.Select(hits[activeHit].Item2,0); }
            else { reader.Select(0, 0); reader.ScrollToCaret(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keyData == Keys.F3 || keyData == (Keys.Shift | Keys.F3)) { MoveHit(keyData == Keys.F3 ? 1 : -1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnThemeChanged()
        {
            if (searchCaption != null) searchCaption.ForeColor = Colors.Dark ? Color.Yellow : Color.Black;
            if (reader == null || sections == null) return;
            sectionScroll.Palette=Colors;sectionScroll.BackColor=Colors.Canvas;readerScroll.Palette=Colors;readerScroll.BackColor=Colors.Surface;reader.BackColor = Colors.Surface; sections.BackColor = Colors.Canvas; reader.ForeColor = sections.ForeColor = Colors.Text;
            if (reader.IsHandleCreated) ShowSection(false);
        }
    }
}
