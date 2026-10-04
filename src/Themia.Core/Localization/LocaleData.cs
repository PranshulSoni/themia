namespace Themia.Core.Localization;

/// <summary>
/// [S-1106] Per-language formatting data, written out because the app is built with
/// <c>InvariantGlobalization</c> (no ICU, no <c>CultureInfo</c> data — S-54). Transcribed from CLDR
/// (the data Windows, ICU and browsers use) for each language's main region: en = US,
/// pt = Brazil, zh = Simplified/China, es = Spain, the rest their home country.
/// <para>
/// Patterns use CLDR letters: <c>d dd</c> day, <c>M MM</c> month number, <c>MMM MMMM</c> month name
/// as used inside a date, <c>LLLL</c> month name on its own (Polish declines it), <c>y</c> year,
/// <c>EEE EEEE</c> weekday, <c>H HH</c> 0–23, <c>h hh</c> 1–12, <c>K</c> 0–11, <c>mm ss</c>,
/// <c>a</c> AM/PM marker; text in single quotes is literal.
/// </para>
/// </summary>
internal sealed class LocaleData
{
    public required string[] Months { get; init; }

    /// <summary>Month names inside a date ("4 października"); same as <see cref="Months"/> unless the language declines them.</summary>
    public string[]? MonthsInDate { get; init; }

    public required string[] MonthsShort { get; init; }

    /// <summary>Sunday first, as <see cref="DayOfWeek"/> counts.</summary>
    public required string[] Weekdays { get; init; }

    public required string[] WeekdaysShort { get; init; }

    public required string[] WeekdaysNarrow { get; init; }

    /// <summary>By <see cref="DateStyle"/>.</summary>
    public required string[] DatePatterns { get; init; }

    public required string Time24 { get; init; }

    public required string Time12 { get; init; }

    public required bool Uses24Hour { get; init; }

    public required string Am { get; init; }

    public required string Pm { get; init; }

    public required char Decimal { get; init; }

    public required string Group { get; init; }

    /// <summary>CLDR minimumGroupingDigits: 2 means 1234 stays ungrouped but 12 345 is grouped (es, pl).</summary>
    public int MinimumGrouping { get; init; } = 1;

    /// <summary>The percent pattern around the number, "{0}%" or "{0} %".</summary>
    public required string Percent { get; init; }

    public required DayOfWeek FirstDayOfWeek { get; init; }

    public required string Now { get; init; }

    /// <summary>Relative times, by <see cref="RelativeUnit"/>: long ("3 minutes ago") and short ("3 min. ago").</summary>
    public required RelativeForms[] Relative { get; init; }

    public required RelativeForms[] RelativeShort { get; init; }

    public static LocaleData For(Locale locale) => locale switch
    {
        Locale.De => De,
        Locale.Es => Es,
        Locale.Fr => Fr,
        Locale.It => It,
        Locale.Ja => Ja,
        Locale.Pl => Pl,
        Locale.Pt => Pt,
        Locale.Zh => Zh,
        _ => En,
    };

    // Relative-time helpers: P(one, other) for the two-form languages, P3 for Polish, P1 for none.
    private static PluralText P(string one, string other) => new(One: one, Other: other);

    private static PluralText P1(string other) => new(Other: other);

    private static PluralText Pl3(string one, string few, string many) => new(One: one, Few: few, Many: many, Other: few);

    private static RelativeForms R(PluralText past, PluralText future) => new(past, future);

    // -----------------------------------------------------------------------------------------

    private static readonly LocaleData En = new()
    {
        Months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
        MonthsShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
        Weekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
        WeekdaysShort = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
        WeekdaysNarrow = ["S", "M", "T", "W", "T", "F", "S"],
        DatePatterns = ["M/d/y", "MMM d, y", "MMMM d, y", "EEEE, MMMM d, y", "MMMM d", "MMM d", "MMMM y", "EEE, MMM d"],
        Time24 = "HH:mm",
        Time12 = "h:mm a",
        Uses24Hour = false,
        Am = "AM",
        Pm = "PM",
        Decimal = '.',
        Group = ",",
        Percent = "{0}%",
        FirstDayOfWeek = DayOfWeek.Sunday,
        Now = "now",
        Relative =
        [
            R(P("{0} minute ago", "{0} minutes ago"), P("in {0} minute", "in {0} minutes")),
            R(P("{0} hour ago", "{0} hours ago"), P("in {0} hour", "in {0} hours")),
            R(P("{0} day ago", "{0} days ago"), P("in {0} day", "in {0} days")),
            R(P("{0} week ago", "{0} weeks ago"), P("in {0} week", "in {0} weeks")),
            R(P("{0} month ago", "{0} months ago"), P("in {0} month", "in {0} months")),
            R(P("{0} year ago", "{0} years ago"), P("in {0} year", "in {0} years")),
        ],
        RelativeShort =
        [
            R(P1("{0} min. ago"), P1("in {0} min.")),
            R(P1("{0} hr. ago"), P1("in {0} hr.")),
            R(P("{0} day ago", "{0} days ago"), P("in {0} day", "in {0} days")),
            R(P1("{0} wk. ago"), P1("in {0} wk.")),
            R(P1("{0} mo. ago"), P1("in {0} mo.")),
            R(P1("{0} yr. ago"), P1("in {0} yr.")),
        ],
    };

    private static readonly LocaleData De = new()
    {
        Months = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"],
        MonthsShort = ["Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sept.", "Okt.", "Nov.", "Dez."],
        Weekdays = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"],
        WeekdaysShort = ["So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa."],
        WeekdaysNarrow = ["S", "M", "D", "M", "D", "F", "S"],
        DatePatterns = ["dd.MM.y", "dd.MM.y", "d. MMMM y", "EEEE, d. MMMM y", "d. MMMM", "d. MMM", "MMMM y", "EEE, d. MMM"],
        Time24 = "HH:mm",
        Time12 = "h:mm a",
        Uses24Hour = true,
        Am = "AM",
        Pm = "PM",
        Decimal = ',',
        Group = ".",
        Percent = "{0}\u00A0%",
        FirstDayOfWeek = DayOfWeek.Monday,
        Now = "jetzt",
        Relative =
        [
            R(P("vor {0} Minute", "vor {0} Minuten"), P("in {0} Minute", "in {0} Minuten")),
            R(P("vor {0} Stunde", "vor {0} Stunden"), P("in {0} Stunde", "in {0} Stunden")),
            R(P("vor {0} Tag", "vor {0} Tagen"), P("in {0} Tag", "in {0} Tagen")),
            R(P("vor {0} Woche", "vor {0} Wochen"), P("in {0} Woche", "in {0} Wochen")),
            R(P("vor {0} Monat", "vor {0} Monaten"), P("in {0} Monat", "in {0} Monaten")),
            R(P("vor {0} Jahr", "vor {0} Jahren"), P("in {0} Jahr", "in {0} Jahren")),
        ],
        RelativeShort =
        [
            R(P1("vor {0} Min."), P1("in {0} Min.")),
            R(P1("vor {0} Std."), P1("in {0} Std.")),
            R(P("vor {0} Tag", "vor {0} Tagen"), P("in {0} Tag", "in {0} Tagen")),
            R(P1("vor {0} Wo."), P1("in {0} Wo.")),
            R(P1("vor {0} Mon."), P1("in {0} Mon.")),
            R(P1("vor {0} J."), P1("in {0} J.")),
        ],
    };

    private static readonly LocaleData Es = new()
    {
        Months = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"],
        MonthsShort = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sept", "oct", "nov", "dic"],
        Weekdays = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"],
        WeekdaysShort = ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"],
        WeekdaysNarrow = ["D", "L", "M", "X", "J", "V", "S"],
        DatePatterns = ["d/M/y", "d MMM y", "d 'de' MMMM 'de' y", "EEEE, d 'de' MMMM 'de' y", "d 'de' MMMM", "d MMM", "MMMM 'de' y", "EEE, d MMM"],
        Time24 = "H:mm",
        Time12 = "h:mm a",
        Uses24Hour = true,
        Am = "a. m.",
        Pm = "p. m.",
        Decimal = ',',
        Group = ".",
        MinimumGrouping = 2,
        Percent = "{0}\u00A0%",
        FirstDayOfWeek = DayOfWeek.Monday,
        Now = "ahora",
        Relative =
        [
            R(P("hace {0} minuto", "hace {0} minutos"), P("dentro de {0} minuto", "dentro de {0} minutos")),
            R(P("hace {0} hora", "hace {0} horas"), P("dentro de {0} hora", "dentro de {0} horas")),
            R(P("hace {0} día", "hace {0} días"), P("dentro de {0} día", "dentro de {0} días")),
            R(P("hace {0} semana", "hace {0} semanas"), P("dentro de {0} semana", "dentro de {0} semanas")),
            R(P("hace {0} mes", "hace {0} meses"), P("dentro de {0} mes", "dentro de {0} meses")),
            R(P("hace {0} año", "hace {0} años"), P("dentro de {0} año", "dentro de {0} años")),
        ],
        RelativeShort =
        [
            R(P1("hace {0} min"), P1("dentro de {0} min")),
            R(P1("hace {0} h"), P1("dentro de {0} h")),
            R(P("hace {0} día", "hace {0} días"), P("dentro de {0} día", "dentro de {0} días")),
            R(P1("hace {0} sem."), P1("dentro de {0} sem.")),
            R(P1("hace {0} m."), P1("dentro de {0} m.")),
            R(P1("hace {0} a"), P1("dentro de {0} a")),
        ],
    };

    private static readonly LocaleData Fr = new()
    {
        Months = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"],
        MonthsShort = ["janv.", "févr.", "mars", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc."],
        Weekdays = ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"],
        WeekdaysShort = ["dim.", "lun.", "mar.", "mer.", "jeu.", "ven.", "sam."],
        WeekdaysNarrow = ["D", "L", "M", "M", "J", "V", "S"],
        DatePatterns = ["dd/MM/y", "d MMM y", "d MMMM y", "EEEE d MMMM y", "d MMMM", "d MMM", "MMMM y", "EEE d MMM"],
        Time24 = "HH:mm",
        Time12 = "h:mm a",
        Uses24Hour = true,
        Am = "AM",
        Pm = "PM",
        Decimal = ',',
        Group = "\u202F",
        Percent = "{0}\u202F%",
        FirstDayOfWeek = DayOfWeek.Monday,
        Now = "maintenant",
        Relative =
        [
            R(P("il y a {0} minute", "il y a {0} minutes"), P("dans {0} minute", "dans {0} minutes")),
            R(P("il y a {0} heure", "il y a {0} heures"), P("dans {0} heure", "dans {0} heures")),
            R(P("il y a {0} jour", "il y a {0} jours"), P("dans {0} jour", "dans {0} jours")),
            R(P("il y a {0} semaine", "il y a {0} semaines"), P("dans {0} semaine", "dans {0} semaines")),
            R(P1("il y a {0} mois"), P1("dans {0} mois")),
            R(P("il y a {0} an", "il y a {0} ans"), P("dans {0} an", "dans {0} ans")),
        ],
        RelativeShort =
        [
            R(P1("il y a {0} min"), P1("dans {0} min")),
            R(P1("il y a {0} h"), P1("dans {0} h")),
            R(P1("il y a {0} j"), P1("dans {0} j")),
            R(P1("il y a {0} sem."), P1("dans {0} sem.")),
            R(P1("il y a {0} m."), P1("dans {0} m.")),
            R(P1("il y a {0} a"), P1("dans {0} a")),
        ],
    };

    private static readonly LocaleData It = new()
    {
        Months = ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"],
        MonthsShort = ["gen", "feb", "mar", "apr", "mag", "giu", "lug", "ago", "set", "ott", "nov", "dic"],
        Weekdays = ["domenica", "lunedì", "martedì", "mercoledì", "giovedì", "venerdì", "sabato"],
        WeekdaysShort = ["dom", "lun", "mar", "mer", "gio", "ven", "sab"],
        WeekdaysNarrow = ["D", "L", "M", "M", "G", "V", "S"],
        DatePatterns = ["dd/MM/y", "d MMM y", "d MMMM y", "EEEE d MMMM y", "d MMMM", "d MMM", "MMMM y", "EEE d MMM"],
        Time24 = "HH:mm",
        Time12 = "h:mm a",
        Uses24Hour = true,
        Am = "AM",
        Pm = "PM",
        Decimal = ',',
        Group = ".",
        Percent = "{0}%",
        FirstDayOfWeek = DayOfWeek.Monday,
        Now = "ora",
        Relative =
        [
            R(P("{0} minuto fa", "{0} minuti fa"), P("tra {0} minuto", "tra {0} minuti")),
            R(P("{0} ora fa", "{0} ore fa"), P("tra {0} ora", "tra {0} ore")),
            R(P("{0} giorno fa", "{0} giorni fa"), P("tra {0} giorno", "tra {0} giorni")),
            R(P("{0} settimana fa", "{0} settimane fa"), P("tra {0} settimana", "tra {0} settimane")),
            R(P("{0} mese fa", "{0} mesi fa"), P("tra {0} mese", "tra {0} mesi")),
            R(P("{0} anno fa", "{0} anni fa"), P("tra {0} anno", "tra {0} anni")),
        ],
        RelativeShort =
        [
            R(P1("{0} min fa"), P1("tra {0} min")),
            R(P1("{0} h fa"), P1("tra {0} h")),
            R(P1("{0} g fa"), P1("tra {0} g")),
            R(P1("{0} sett. fa"), P1("tra {0} sett.")),
            R(P("{0} mese fa", "{0} mesi fa"), P("tra {0} mese", "tra {0} mesi")),
            R(P("{0} anno fa", "{0} anni fa"), P("tra {0} anno", "tra {0} anni")),
        ],
    };

    private static readonly LocaleData Ja = new()
    {
        Months = ["1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月"],
        MonthsShort = ["1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月"],
        Weekdays = ["日曜日", "月曜日", "火曜日", "水曜日", "木曜日", "金曜日", "土曜日"],
        WeekdaysShort = ["日", "月", "火", "水", "木", "金", "土"],
        WeekdaysNarrow = ["日", "月", "火", "水", "木", "金", "土"],
        DatePatterns = ["y/MM/dd", "y/MM/dd", "y年M月d日", "y年M月d日EEEE", "M月d日", "M月d日", "y年M月", "M月d日(EEE)"],
        Time24 = "H:mm",
        Time12 = "aK:mm",
        Uses24Hour = true,
        Am = "午前",
        Pm = "午後",
        Decimal = '.',
        Group = ",",
        Percent = "{0}%",
        FirstDayOfWeek = DayOfWeek.Sunday,
        Now = "今",
        Relative =
        [
            R(P1("{0} 分前"), P1("{0} 分後")),
            R(P1("{0} 時間前"), P1("{0} 時間後")),
            R(P1("{0} 日前"), P1("{0} 日後")),
            R(P1("{0} 週間前"), P1("{0} 週間後")),
            R(P1("{0} か月前"), P1("{0} か月後")),
            R(P1("{0} 年前"), P1("{0} 年後")),
        ],
        RelativeShort =
        [
            R(P1("{0} 分前"), P1("{0} 分後")),
            R(P1("{0} 時間前"), P1("{0} 時間後")),
            R(P1("{0} 日前"), P1("{0} 日後")),
            R(P1("{0} 週間前"), P1("{0} 週間後")),
            R(P1("{0} か月前"), P1("{0} か月後")),
            R(P1("{0} 年前"), P1("{0} 年後")),
        ],
    };

    private static readonly LocaleData Pl = new()
    {
        Months = ["styczeń", "luty", "marzec", "kwiecień", "maj", "czerwiec", "lipiec", "sierpień", "wrzesień", "październik", "listopad", "grudzień"],
        MonthsInDate = ["stycznia", "lutego", "marca", "kwietnia", "maja", "czerwca", "lipca", "sierpnia", "września", "października", "listopada", "grudnia"],
        MonthsShort = ["sty", "lut", "mar", "kwi", "maj", "cze", "lip", "sie", "wrz", "paź", "lis", "gru"],
        Weekdays = ["niedziela", "poniedziałek", "wtorek", "środa", "czwartek", "piątek", "sobota"],
        WeekdaysShort = ["niedz.", "pon.", "wt.", "śr.", "czw.", "pt.", "sob."],
        WeekdaysNarrow = ["N", "P", "W", "Ś", "C", "P", "S"],
        DatePatterns = ["dd.MM.y", "d MMM y", "d MMMM y", "EEEE, d MMMM y", "d MMMM", "d MMM", "LLLL y", "EEE, d MMM"],
        Time24 = "HH:mm",
        Time12 = "h:mm a",
        Uses24Hour = true,
        Am = "AM",
        Pm = "PM",
        Decimal = ',',
        Group = "\u00A0",
        MinimumGrouping = 2,
        Percent = "{0}%",
        FirstDayOfWeek = DayOfWeek.Monday,
        Now = "teraz",
        Relative =
        [
            R(Pl3("{0} minutę temu", "{0} minuty temu", "{0} minut temu"), Pl3("za {0} minutę", "za {0} minuty", "za {0} minut")),
            R(Pl3("{0} godzinę temu", "{0} godziny temu", "{0} godzin temu"), Pl3("za {0} godzinę", "za {0} godziny", "za {0} godzin")),
            R(Pl3("{0} dzień temu", "{0} dni temu", "{0} dni temu"), Pl3("za {0} dzień", "za {0} dni", "za {0} dni")),
            R(Pl3("{0} tydzień temu", "{0} tygodnie temu", "{0} tygodni temu"), Pl3("za {0} tydzień", "za {0} tygodnie", "za {0} tygodni")),
            R(Pl3("{0} miesiąc temu", "{0} miesiące temu", "{0} miesięcy temu"), Pl3("za {0} miesiąc", "za {0} miesiące", "za {0} miesięcy")),
            R(Pl3("{0} rok temu", "{0} lata temu", "{0} lat temu"), Pl3("za {0} rok", "za {0} lata", "za {0} lat")),
        ],
        RelativeShort =
        [
            R(P1("{0} min temu"), P1("za {0} min")),
            R(P1("{0} godz. temu"), P1("za {0} godz.")),
            R(Pl3("{0} dzień temu", "{0} dni temu", "{0} dni temu"), Pl3("za {0} dzień", "za {0} dni", "za {0} dni")),
            R(P1("{0} tydz. temu"), P1("za {0} tydz.")),
            R(P1("{0} mies. temu"), P1("za {0} mies.")),
            R(Pl3("{0} rok temu", "{0} lata temu", "{0} lat temu"), Pl3("za {0} rok", "za {0} lata", "za {0} lat")),
        ],
    };

    private static readonly LocaleData Pt = new()
    {
        Months = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"],
        MonthsShort = ["jan.", "fev.", "mar.", "abr.", "mai.", "jun.", "jul.", "ago.", "set.", "out.", "nov.", "dez."],
        Weekdays = ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"],
        WeekdaysShort = ["dom.", "seg.", "ter.", "qua.", "qui.", "sex.", "sáb."],
        WeekdaysNarrow = ["D", "S", "T", "Q", "Q", "S", "S"],
        DatePatterns = ["dd/MM/y", "d 'de' MMM 'de' y", "d 'de' MMMM 'de' y", "EEEE, d 'de' MMMM 'de' y", "d 'de' MMMM", "d 'de' MMM", "MMMM 'de' y", "EEE, d 'de' MMM"],
        Time24 = "HH:mm",
        Time12 = "h:mm a",
        Uses24Hour = true,
        Am = "AM",
        Pm = "PM",
        Decimal = ',',
        Group = ".",
        Percent = "{0}%",
        FirstDayOfWeek = DayOfWeek.Sunday,
        Now = "agora",
        Relative =
        [
            R(P("há {0} minuto", "há {0} minutos"), P("em {0} minuto", "em {0} minutos")),
            R(P("há {0} hora", "há {0} horas"), P("em {0} hora", "em {0} horas")),
            R(P("há {0} dia", "há {0} dias"), P("em {0} dia", "em {0} dias")),
            R(P("há {0} semana", "há {0} semanas"), P("em {0} semana", "em {0} semanas")),
            R(P("há {0} mês", "há {0} meses"), P("em {0} mês", "em {0} meses")),
            R(P("há {0} ano", "há {0} anos"), P("em {0} ano", "em {0} anos")),
        ],
        RelativeShort =
        [
            R(P1("há {0} min."), P1("em {0} min.")),
            R(P1("há {0} h"), P1("em {0} h")),
            R(P("há {0} dia", "há {0} dias"), P("em {0} dia", "em {0} dias")),
            R(P1("há {0} sem."), P1("em {0} sem.")),
            R(P("há {0} mês", "há {0} meses"), P("em {0} mês", "em {0} meses")),
            R(P("há {0} ano", "há {0} anos"), P("em {0} ano", "em {0} anos")),
        ],
    };

    private static readonly LocaleData Zh = new()
    {
        Months = ["一月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "十一月", "十二月"],
        MonthsShort = ["1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月"],
        Weekdays = ["星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"],
        WeekdaysShort = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"],
        WeekdaysNarrow = ["日", "一", "二", "三", "四", "五", "六"],
        DatePatterns = ["y/M/d", "y年M月d日", "y年M月d日", "y年M月d日EEEE", "M月d日", "M月d日", "y年M月", "M月d日EEE"],
        Time24 = "HH:mm",
        Time12 = "ah:mm",
        Uses24Hour = true,
        Am = "上午",
        Pm = "下午",
        Decimal = '.',
        Group = ",",
        Percent = "{0}%",
        FirstDayOfWeek = DayOfWeek.Monday,
        Now = "现在",
        Relative =
        [
            R(P1("{0}分钟前"), P1("{0}分钟后")),
            R(P1("{0}小时前"), P1("{0}小时后")),
            R(P1("{0}天前"), P1("{0}天后")),
            R(P1("{0}周前"), P1("{0}周后")),
            R(P1("{0}个月前"), P1("{0}个月后")),
            R(P1("{0}年前"), P1("{0}年后")),
        ],
        RelativeShort =
        [
            R(P1("{0}分钟前"), P1("{0}分钟后")),
            R(P1("{0}小时前"), P1("{0}小时后")),
            R(P1("{0}天前"), P1("{0}天后")),
            R(P1("{0}周前"), P1("{0}周后")),
            R(P1("{0}个月前"), P1("{0}个月后")),
            R(P1("{0}年前"), P1("{0}年后")),
        ],
    };
}

/// <summary>One text per plural category; a missing category uses <see cref="Other"/>.</summary>
internal readonly record struct PluralText(string Other, string? One = null, string? Few = null, string? Many = null)
{
    public string For(PluralCategory category) => category switch
    {
        PluralCategory.One => One ?? Other,
        PluralCategory.Few => Few ?? Other,
        PluralCategory.Many => Many ?? Other,
        _ => Other,
    };
}

/// <summary>"… ago" and "in …" for one unit.</summary>
internal readonly record struct RelativeForms(PluralText Past, PluralText Future);
