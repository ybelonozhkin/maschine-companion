#include <Windows.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Devices.Midi2.h>
#include <winrt/Windows.Devices.Midi2.Transports.Loopback.h>

using namespace winrt::Windows::Devices::Midi2;
using namespace winrt::Windows::Devices::Midi2::Transports::Loopback;
static winrt::guid association{};
static bool created = false;
static DWORD ownerThread = 0;
static HANDLE ownerMutex = nullptr;
static void release_owner() noexcept {
    if (ownerMutex) { ReleaseMutex(ownerMutex); CloseHandle(ownerMutex); ownerMutex = nullptr; }
}

extern "C" __declspec(dllexport) int __cdecl MC_Create(wchar_t* error, int capacity) noexcept {
    try {
        if (created) return 0;
        ownerMutex = CreateMutexW(nullptr, FALSE, L"Local\\MaschineCompanionMidiOwner");
        if (!ownerMutex) winrt::throw_last_error();
        DWORD wait = WaitForSingleObject(ownerMutex, 0);
        if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED) {
            CloseHandle(ownerMutex); ownerMutex = nullptr;
            winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_BUSY));
        }
        winrt::init_apartment(winrt::apartment_type::single_threaded);
        ownerThread = GetCurrentThreadId();
        if (!MidiApi::EnsureServiceAvailable() || !MidiLoopbackManager::IsTransportAvailable())
            winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED));
        // Adopt only our exact private IDs after an interrupted exit.
        for (auto const& entry : MidiLoopbackManager::GetActiveLoopbackEntries()) {
            if (entry.EndpointA().EndpointDeviceId() == L"\\\\?\\swd#midisrv#midiu_loop_a_maschine-companion-native-a#{e7cce071-3c03-423f-88d3-f1045d02552b}" &&
                entry.EndpointB().EndpointDeviceId() == L"\\\\?\\swd#midisrv#midiu_loop_b_maschine-companion-native-b#{e7cce071-3c03-423f-88d3-f1045d02552b}") {
                association = entry.AssociationId(); created = true; return 0;
            }
        }
        MidiLoopbackEndpointDefinition a, b;
        a.Name(L"Mikro Companion Send"); a.UniqueId(L"maschine-companion-native-a");
        b.Name(L"Mikro Companion"); b.UniqueId(L"maschine-companion-native-b");
        auto response = MidiLoopbackManager::CreateTransientLoopback(MidiLoopbackCreationConfig(a, b));
        if (!response.Success()) {
            wcsncpy_s(error, capacity, response.ErrorMessage().c_str(), _TRUNCATE);
            winrt::uninit_apartment(); ownerThread = 0; release_owner(); return 1;
        }
        association = response.CreatedLoopbackEntry().AssociationId(); created = true; return 0;
    } catch (winrt::hresult_error const& e) {
        wcsncpy_s(error, capacity, e.message().c_str(), _TRUNCATE);
        if (ownerThread) { winrt::uninit_apartment(); ownerThread = 0; }
        release_owner();
        return e.code().value;
    } catch (...) {
        if (ownerThread) { winrt::uninit_apartment(); ownerThread = 0; }
        release_owner();
        return -1;
    }
}
extern "C" __declspec(dllexport) int __cdecl MC_Close() noexcept {
    if (!created) return 0;
    if (GetCurrentThreadId() != ownerThread) return -2;
    try {
        bool success = MidiLoopbackManager::RemoveTransientLoopback(MidiLoopbackRemovalConfig(association)).Success();
        if (!success) return 1;
        created = false; winrt::uninit_apartment(); ownerThread = 0; release_owner(); return 0;
    } catch (...) { return -1; }
}
