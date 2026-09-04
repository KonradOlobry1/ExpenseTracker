namespace ExpenseTracker.Localization;

public static class Translations
{
    // ── EnglishTranslation ──────────────────────────────────────────────────────────────
    private static readonly IReadOnlyDictionary<string, string> EnglishTranslation =
        new Dictionary<string, string>
        {
            // App / Nav
            ["AppTitle"]                  = "Expense Tracker",
            ["Nav_Dashboard"]             = "Dashboard",
            ["Nav_Expenses"]              = "Expenses",
            ["Nav_Income"]                = "Income",
            ["Nav_Subscriptions"]         = "Subscriptions",
            ["Nav_Analytics"]             = "Analytics",
            ["Nav_Settings"]              = "Settings",

            // Theme
            ["Theme_SwitchToDark"]        = "Switch to Dark Mode",
            ["Theme_SwitchToLight"]       = "Switch to Light Mode",
            ["Theme_DarkMode"]            = "Dark Mode",
            ["Theme_DarkActive"]          = "Dark theme active",
            ["Theme_LightActive"]         = "Light theme active",

            // Common
            ["Common_Cancel"]             = "Cancel",
            ["Common_Save"]               = "Save",
            ["Common_Delete"]             = "Delete",
            ["Common_Clear"]              = "Clear",
            ["Common_Search"]             = "Search",
            ["Common_Actions"]            = "Actions",
            ["Common_Date"]               = "Date",
            ["Common_Name"]               = "Name",
            ["Common_Amount"]             = "Amount",
            ["Common_Category"]           = "Category",
            ["Common_Notes"]              = "Notes",
            ["Common_Active"]             = "Active",
            ["Common_Inactive"]           = "Inactive",
            ["Common_Total"]              = "Total:",
            ["Common_Description"]        = "Description",
            ["Common_Status"]             = "Status",

            // Dashboard
            ["Dashboard_Title"]           = "Dashboard",
            ["Dashboard_ThisMonth"]       = "This Month",
            ["Dashboard_MonthlyIncome"]   = "Monthly Income",
            ["Dashboard_Remaining"]       = "Remaining:",
            ["Dashboard_OfIncome"]        = "of income spent",
            ["Dashboard_MonthlySubs"]     = "Monthly Subscriptions",
            ["Dashboard_ActiveCount"]     = "active",
            ["Dashboard_LargestCat"]      = "Largest Category",
            ["Dashboard_ByCategory"]      = "This Month by Category",
            ["Dashboard_NoExpenses"]      = "No expenses this month",

            // Income page
            ["Income_Title"]              = "Income",
            ["Income_NoIncome"]           = "No income entries found.",
            ["Income_AddIncome"]          = "Add Income",
            ["Income_EditIncome"]         = "Edit Income",
            ["Income_DeleteIncome"]       = "Delete Income",
            ["Income_Added"]              = "Income added.",
            ["Income_Updated"]            = "Income updated.",
            ["Income_Deleted"]            = "Income deleted.",
            ["Income_DeleteConfirm"]      = "Delete \"{0}\"?",
            ["Income_MonthlyTotal"]       = "Monthly equivalent (active):",

            // Expenses page
            ["Expenses_Title"]            = "Expenses",
            ["Expenses_From"]             = "From",
            ["Expenses_To"]               = "To",
            ["Expenses_ApplyFilter"]      = "Apply Filter",
            ["Expenses_NoExpenses"]       = "No expenses found.",
            ["Expenses_AddExpense"]       = "Add Expense",
            ["Expenses_EditExpense"]      = "Edit Expense",
            ["Expenses_DeleteExpense"]    = "Delete Expense",
            ["Expenses_Added"]            = "Expense added.",
            ["Expenses_Updated"]          = "Expense updated.",
            ["Expenses_Deleted"]          = "Expense deleted.",
            ["Expenses_DeleteConfirm"]    = "Delete \"{0}\"?",

            // Subscriptions page
            ["Subs_Title"]                = "Subscriptions",
            ["Subs_BurnRate"]             = "Monthly Burn Rate",
            ["Subs_ActiveSubs"]           = "Active Subscriptions",
            ["Subs_NextDue"]              = "Next Due",
            ["Subs_ShowInactive"]         = "Show inactive",
            ["Subs_Cycle"]                = "Cycle",
            ["Subs_MonthlyEquiv"]         = "Monthly Equiv.",
            ["Subs_NextPayment"]          = "Next Payment",
            ["Subs_NoSubs"]               = "No subscriptions found.",
            ["Subs_AddSub"]               = "Add Subscription",
            ["Subs_EditSub"]              = "Edit Subscription",
            ["Subs_DeleteSub"]            = "Delete Subscription",
            ["Subs_Added"]                = "Subscription added.",
            ["Subs_Updated"]              = "Subscription updated.",
            ["Subs_Deleted"]              = "Subscription deleted.",
            ["Subs_DeleteConfirm"]        = "Delete \"{0}\"?",

            // Analytics page
            ["Analytics_Title"]           = "Analytics",
            ["Analytics_Year"]            = "Year",
            ["Analytics_MonthlySpending"] = "Monthly Spending — {0}",
            ["Analytics_YoY"]             = "Year-over-Year Comparison",
            ["Analytics_CategoryBreakdown"] = "Category Breakdown — {0}",
            ["Analytics_PercentOfYear"]   = "% of Year",
            ["Analytics_NoData"]          = "No data for {0}.",

            // Settings page
            ["Settings_Title"]            = "Settings",
            ["Settings_Appearance"]       = "Appearance",
            ["Settings_Currency"]         = "Currency",
            ["Settings_DisplayCurrency"]  = "Display Currency",
            ["Settings_Preview"]          = "Preview:",
            ["Settings_Language"]         = "Language",
            ["Settings_DisplayLanguage"]  = "Display Language",

            // Dialogs
            ["Dlg_DescriptionRequired"]   = "Description is required",
            ["Dlg_AmountRequired"]        = "Amount is required",
            ["Dlg_NameRequired"]          = "Name is required",
            ["Dlg_BillingCycle"]          = "Billing Cycle",
            ["Dlg_MonthlyEquiv"]          = "Monthly equivalent:",
            ["Dlg_StartDate"]             = "Start Date",
            ["Dlg_EndDate"]               = "End Date (optional)",
            ["Dlg_Website"]               = "Website (optional)",

            // Charts
            ["Chart_SpendingTitle"]       = "Spending — Last 6 Months",
            ["Chart_Expenses"]            = "Expenses",
            ["Chart_UpcomingTitle"]       = "Upcoming Payments — Next 90 Days",
            ["Chart_NoActiveSubs"]        = "No active subscriptions.",
            ["Chart_BarsNote"]            = "Bars indicate payment due on that week",

            // Not Found
            ["NotFound_Title"]            = "Not Found",
            ["NotFound_Message"]          = "Sorry, the content you are looking for does not exist.",

            // Login / Auth
            ["Login_Title"]               = "Account",
            ["Login_Email"]               = "Email",
            ["Login_Password"]            = "Password",
            ["Login_ConfirmPassword"]     = "Confirm Password",
            ["Login_SignIn"]              = "Sign In",
            ["Login_Register"]            = "Register",
            ["Login_InvalidCredentials"]  = "Invalid email or password.",
            ["Login_SessionExpired"]      = "Your session has expired. Sign in again to sync.",
            ["Login_RegisterSuccess"]     = "Account created. You are now signed in.",
            ["Login_AccountLocked"]       = "Too many failed attempts. Try again in a few minutes.",
            ["Login_NetworkError"]        = "Could not reach the server. Check your connection and try again.",
            ["Login_ServerError"]         = "Something went wrong on our end. Try again shortly.",

            // Account & Sync settings
            ["Settings_Account"]          = "Account & Sync",
            ["Settings_NotSignedIn"]      = "Not signed in",
            ["Settings_SignInButton"]     = "Sign In / Register",
            ["Settings_SignedInAs"]       = "Signed in as",
            ["Settings_SyncNow"]         = "Sync Now",
            ["Settings_LastSync"]         = "Last synced:",
            ["Settings_SignOut"]          = "Sign Out",
            ["Settings_NeverSynced"]      = "Never synced",
            ["Settings_NoSyncNeeded"]     = "Your changes are saved directly to the cloud. There is nothing to sync here.",
            ["Settings_Syncing"]          = "Syncing...",

            // Payment detection
            ["Settings_PaymentDetect"]    = "Payment Detection",
            ["Settings_PaymentGranted"]   = "Permission granted",
            ["Settings_PaymentDenied"]    = "Permission not granted",
            ["Settings_PaymentGrant"]     = "Grant Permission",
            ["Settings_PaymentInfo"]      = "Reads Google Pay notifications to suggest adding expenses automatically.",
            ["Sync_PaymentDetected"]      = "Payment detected",
            ["Sync_PaymentPrompt"]        = "Add {0} at {1} as an expense?",
            ["Sync_AddExpense"]           = "Add Expense",
            ["Sync_Dismiss"]              = "Dismiss",
            ["Sync_NetworkError"]         = "Could not reach the server. Your changes are saved and will sync later.",
            ["Sync_ServerError"]          = "Sync failed. Try again shortly.",
            ["Sync_LocalDatabaseError"]   = "Sync failed while saving to this device. Try again.",

            // Categories (system only — user categories fall back to their raw name)
            ["Category_Food"]             = "Food",
            ["Category_Transport"]        = "Transport",
            ["Category_Housing"]          = "Housing",
            ["Category_Health"]           = "Health",
            ["Category_Entertainment"]    = "Entertainment",
            ["Category_Utilities"]        = "Utilities",
            ["Category_Other"]            = "Other",

            // Billing cycles (BillingCycle enum member names)
            ["Cycle_Weekly"]              = "Weekly",
            ["Cycle_Monthly"]             = "Monthly",
            ["Cycle_Quarterly"]           = "Quarterly",
            ["Cycle_Yearly"]              = "Yearly",

            // Categories management
            ["Nav_Categories"]            = "Categories",
            ["Categories_Title"]          = "Categories",
            ["Categories_Add"]            = "Add Category",
            ["Categories_Edit"]           = "Edit Category",
            ["Categories_Delete"]         = "Delete Category",
            ["Categories_DeleteConfirm"]  = "Delete \"{0}\"? Expenses and subscriptions in this category stay in the database but will no longer be shown.",
            ["Categories_None"]           = "No categories yet.",
            ["Categories_Added"]          = "Category added",
            ["Categories_Updated"]        = "Category updated",
            ["Categories_Deleted"]        = "Category deleted",
            ["Categories_Builtin"]        = "Built-in",
            ["Categories_BuiltinLocked"]  = "Built-in categories cannot be deleted",
            ["Cat_Icon"]                  = "Icon",
            ["Cat_Color"]                 = "Colour",
            ["Cat_Preview"]               = "Preview",
        };

    // ── PolishTranslation ───────────────────────────────────────────────────────────────
    private static readonly IReadOnlyDictionary<string, string> PolishTranslation =
        new Dictionary<string, string>
        {
            // App / Nav
            ["AppTitle"]                  = "Śledzenie Wydatków",
            ["Nav_Dashboard"]             = "Panel główny",
            ["Nav_Expenses"]              = "Wydatki",
            ["Nav_Income"]                = "Przychody",
            ["Nav_Subscriptions"]         = "Subskrypcje",
            ["Nav_Analytics"]             = "Analityka",
            ["Nav_Settings"]              = "Ustawienia",

            // Theme
            ["Theme_SwitchToDark"]        = "Przełącz na tryb ciemny",
            ["Theme_SwitchToLight"]       = "Przełącz na tryb jasny",
            ["Theme_DarkMode"]            = "Tryb ciemny",
            ["Theme_DarkActive"]          = "Aktywny tryb ciemny",
            ["Theme_LightActive"]         = "Aktywny tryb jasny",

            // Common
            ["Common_Cancel"]             = "Anuluj",
            ["Common_Save"]               = "Zapisz",
            ["Common_Delete"]             = "Usuń",
            ["Common_Clear"]              = "Wyczyść",
            ["Common_Search"]             = "Szukaj",
            ["Common_Actions"]            = "Akcje",
            ["Common_Date"]               = "Data",
            ["Common_Name"]               = "Nazwa",
            ["Common_Amount"]             = "Kwota",
            ["Common_Category"]           = "Kategoria",
            ["Common_Notes"]              = "Notatki",
            ["Common_Active"]             = "Aktywna",
            ["Common_Inactive"]           = "Nieaktywna",
            ["Common_Total"]              = "Łącznie:",
            ["Common_Description"]        = "Opis",
            ["Common_Status"]             = "Status",

            // Dashboard
            ["Dashboard_Title"]           = "Panel główny",
            ["Dashboard_ThisMonth"]       = "Ten miesiąc",
            ["Dashboard_MonthlyIncome"]   = "Przychód miesięczny",
            ["Dashboard_Remaining"]       = "Pozostało:",
            ["Dashboard_OfIncome"]        = "przychodu wydane",
            ["Dashboard_MonthlySubs"]     = "Miesięczne subskrypcje",
            ["Dashboard_ActiveCount"]     = "aktywnych",
            ["Dashboard_LargestCat"]      = "Największa kategoria",
            ["Dashboard_ByCategory"]      = "Ten miesiąc wg kategorii",
            ["Dashboard_NoExpenses"]      = "Brak wydatków w tym miesiącu",

            // Income page
            ["Income_Title"]              = "Przychody",
            ["Income_NoIncome"]           = "Nie znaleziono przychodów.",
            ["Income_AddIncome"]          = "Dodaj przychód",
            ["Income_EditIncome"]         = "Edytuj przychód",
            ["Income_DeleteIncome"]       = "Usuń przychód",
            ["Income_Added"]              = "Przychód dodany.",
            ["Income_Updated"]            = "Przychód zaktualizowany.",
            ["Income_Deleted"]            = "Przychód usunięty.",
            ["Income_DeleteConfirm"]      = "Usunąć \"{0}\"?",
            ["Income_MonthlyTotal"]       = "Równoważnik miesięczny (aktywne):",

            // Expenses page
            ["Expenses_Title"]            = "Wydatki",
            ["Expenses_From"]             = "Od",
            ["Expenses_To"]               = "Do",
            ["Expenses_ApplyFilter"]      = "Zastosuj filtr",
            ["Expenses_NoExpenses"]       = "Nie znaleziono wydatków.",
            ["Expenses_AddExpense"]       = "Dodaj wydatek",
            ["Expenses_EditExpense"]      = "Edytuj wydatek",
            ["Expenses_DeleteExpense"]    = "Usuń wydatek",
            ["Expenses_Added"]            = "Wydatek dodany.",
            ["Expenses_Updated"]          = "Wydatek zaktualizowany.",
            ["Expenses_Deleted"]          = "Wydatek usunięty.",
            ["Expenses_DeleteConfirm"]    = "Usunąć \"{0}\"?",

            // Subscriptions page
            ["Subs_Title"]                = "Subskrypcje",
            ["Subs_BurnRate"]             = "Miesięczne koszty",
            ["Subs_ActiveSubs"]           = "Aktywne subskrypcje",
            ["Subs_NextDue"]              = "Następna płatność",
            ["Subs_ShowInactive"]         = "Pokaż nieaktywne",
            ["Subs_Cycle"]                = "Cykl",
            ["Subs_MonthlyEquiv"]         = "Równow. mies.",
            ["Subs_NextPayment"]          = "Następna płatność",
            ["Subs_NoSubs"]               = "Nie znaleziono subskrypcji.",
            ["Subs_AddSub"]               = "Dodaj subskrypcję",
            ["Subs_EditSub"]              = "Edytuj subskrypcję",
            ["Subs_DeleteSub"]            = "Usuń subskrypcję",
            ["Subs_Added"]                = "Subskrypcja dodana.",
            ["Subs_Updated"]              = "Subskrypcja zaktualizowana.",
            ["Subs_Deleted"]              = "Subskrypcja usunięta.",
            ["Subs_DeleteConfirm"]        = "Usunąć \"{0}\"?",

            // Analytics page
            ["Analytics_Title"]           = "Analityka",
            ["Analytics_Year"]            = "Rok",
            ["Analytics_MonthlySpending"] = "Miesięczne wydatki — {0}",
            ["Analytics_YoY"]             = "Porównanie rok do roku",
            ["Analytics_CategoryBreakdown"] = "Zestawienie kategorii — {0}",
            ["Analytics_PercentOfYear"]   = "% roku",
            ["Analytics_NoData"]          = "Brak danych dla {0}.",

            // Settings page
            ["Settings_Title"]            = "Ustawienia",
            ["Settings_Appearance"]       = "Wygląd",
            ["Settings_Currency"]         = "Waluta",
            ["Settings_DisplayCurrency"]  = "Wyświetlana waluta",
            ["Settings_Preview"]          = "Podgląd:",
            ["Settings_Language"]         = "Język",
            ["Settings_DisplayLanguage"]  = "Język wyświetlania",

            // Dialogs
            ["Dlg_DescriptionRequired"]   = "Opis jest wymagany",
            ["Dlg_AmountRequired"]        = "Kwota jest wymagana",
            ["Dlg_NameRequired"]          = "Nazwa jest wymagana",
            ["Dlg_BillingCycle"]          = "Cykl rozliczeniowy",
            ["Dlg_MonthlyEquiv"]          = "Równoważnik miesięczny:",
            ["Dlg_StartDate"]             = "Data rozpoczęcia",
            ["Dlg_EndDate"]               = "Data zakończenia (opcjonalna)",
            ["Dlg_Website"]               = "Strona WWW (opcjonalna)",

            // Charts
            ["Chart_SpendingTitle"]       = "Wydatki — ostatnie 6 miesięcy",
            ["Chart_Expenses"]            = "Wydatki",
            ["Chart_UpcomingTitle"]       = "Nadchodzące płatności — następne 90 dni",
            ["Chart_NoActiveSubs"]        = "Brak aktywnych subskrypcji.",
            ["Chart_BarsNote"]            = "Słupki oznaczają płatność w danym tygodniu",

            // Not Found
            ["NotFound_Title"]            = "Nie znaleziono",
            ["NotFound_Message"]          = "Przepraszamy, szukana strona nie istnieje.",

            // Login / Auth
            ["Login_Title"]               = "Konto",
            ["Login_Email"]               = "E-mail",
            ["Login_Password"]            = "Hasło",
            ["Login_ConfirmPassword"]     = "Potwierdź hasło",
            ["Login_SignIn"]              = "Zaloguj się",
            ["Login_Register"]            = "Zarejestruj się",
            ["Login_InvalidCredentials"]  = "Nieprawidłowy e-mail lub hasło.",
            ["Login_SessionExpired"]      = "Sesja wygasła. Zaloguj się ponownie, aby zsynchronizować.",
            ["Login_RegisterSuccess"]     = "Konto utworzone. Jesteś zalogowany.",
            ["Login_AccountLocked"]       = "Zbyt wiele nieudanych prób. Spróbuj ponownie za kilka minut.",
            ["Login_NetworkError"]        = "Nie można połączyć się z serwerem. Sprawdź połączenie i spróbuj ponownie.",
            ["Login_ServerError"]         = "Coś poszło nie tak po naszej stronie. Spróbuj ponownie wkrótce.",

            // Account & Sync settings
            ["Settings_Account"]          = "Konto i synchronizacja",
            ["Settings_NotSignedIn"]      = "Nie zalogowano",
            ["Settings_SignInButton"]     = "Zaloguj / Zarejestruj się",
            ["Settings_SignedInAs"]       = "Zalogowano jako",
            ["Settings_SyncNow"]         = "Synchronizuj teraz",
            ["Settings_LastSync"]         = "Ostatnia sync.:",
            ["Settings_SignOut"]          = "Wyloguj się",
            ["Settings_NeverSynced"]      = "Nigdy nie synchronizowano",
            ["Settings_NoSyncNeeded"]     = "Zmiany są zapisywane bezpośrednio w chmurze. Nie ma tu nic do zsynchronizowania.",
            ["Settings_Syncing"]          = "Synchronizuję...",

            // Payment detection
            ["Settings_PaymentDetect"]    = "Wykrywanie płatności",
            ["Settings_PaymentGranted"]   = "Uprawnienie przyznane",
            ["Settings_PaymentDenied"]    = "Brak uprawnienia",
            ["Settings_PaymentGrant"]     = "Przyznaj uprawnienie",
            ["Settings_PaymentInfo"]      = "Odczytuje powiadomienia Google Pay, aby automatycznie proponować dodanie wydatku.",
            ["Sync_PaymentDetected"]      = "Wykryto płatność",
            ["Sync_PaymentPrompt"]        = "Dodać {0} w {1} jako wydatek?",
            ["Sync_AddExpense"]           = "Dodaj wydatek",
            ["Sync_Dismiss"]              = "Odrzuć",
            ["Sync_NetworkError"]         = "Nie można połączyć się z serwerem. Zmiany są zapisane i zsynchronizują się później.",
            ["Sync_ServerError"]          = "Synchronizacja nie powiodła się. Spróbuj ponownie wkrótce.",
            ["Sync_LocalDatabaseError"]   = "Synchronizacja nie powiodła się podczas zapisu na tym urządzeniu. Spróbuj ponownie.",

            // Categories (system only — user categories fall back to their raw name)
            ["Category_Food"]             = "Jedzenie",
            ["Category_Transport"]        = "Transport",
            ["Category_Housing"]          = "Mieszkanie",
            ["Category_Health"]           = "Zdrowie",
            ["Category_Entertainment"]    = "Rozrywka",
            ["Category_Utilities"]        = "Media",
            ["Category_Other"]            = "Inne",

            // Billing cycles (BillingCycle enum member names)
            ["Cycle_Weekly"]              = "Tygodniowo",
            ["Cycle_Monthly"]             = "Miesięcznie",
            ["Cycle_Quarterly"]           = "Kwartalnie",
            ["Cycle_Yearly"]              = "Rocznie",

            // Categories management
            ["Nav_Categories"]            = "Kategorie",
            ["Categories_Title"]          = "Kategorie",
            ["Categories_Add"]            = "Dodaj kategorię",
            ["Categories_Edit"]           = "Edytuj kategorię",
            ["Categories_Delete"]         = "Usuń kategorię",
            ["Categories_DeleteConfirm"]  = "Usunąć \"{0}\"? Wydatki i subskrypcje w tej kategorii pozostaną w bazie, ale przestaną być widoczne.",
            ["Categories_None"]           = "Brak kategorii.",
            ["Categories_Added"]          = "Dodano kategorię",
            ["Categories_Updated"]        = "Zaktualizowano kategorię",
            ["Categories_Deleted"]        = "Usunięto kategorię",
            ["Categories_Builtin"]        = "Wbudowana",
            ["Categories_BuiltinLocked"]  = "Wbudowanych kategorii nie można usunąć",
            ["Cat_Icon"]                  = "Ikona",
            ["Cat_Color"]                 = "Kolor",
            ["Cat_Preview"]               = "Podgląd",
        };

    // ── All — declared LAST so both dictionaries above are already initialized ──────────
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> All =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            { "en", EnglishTranslation },
            { "pl", PolishTranslation  },
        };
}
