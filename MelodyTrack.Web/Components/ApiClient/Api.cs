namespace MelodyTrack.Web.Components.ApiClient;

public class Api
{
    public AuthApi Auth { get; set; }
    public ClientsApi Clients { get; set; }
    public ExpensesApi Expenses { get; set; }
    public PaymentsApi Payments { get; set; }
    public ScheduleApi Schedule { get; set; }
    public ServicesApi Services { get; set; }
    public UsersApi Users { get; set; }
    public ApiUtils Utils { get; set; }

    public Api(AuthApi auth, ClientsApi clients, ExpensesApi expenses, PaymentsApi payments, ScheduleApi schedule, ServicesApi services, UsersApi users, ApiUtils utils)
    {
        Auth = auth;
        Clients = clients;
        Expenses = expenses;
        Payments = payments;
        Schedule = schedule;
        Services = services;
        Users = users;
        Utils = utils;
    }
}