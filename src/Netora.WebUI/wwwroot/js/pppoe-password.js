document.querySelectorAll('form.pppoe-password').forEach(form => {
    const value = form.querySelector('[data-password-value]');
    const button = form.querySelector('button');
    const error = form.querySelector('[data-password-error]');
    const mask = value.textContent;
    let revealed = false;
    let request;

    function conceal() {
        request?.abort();
        value.textContent = mask;
        button.textContent = 'Show password';
        revealed = false;
    }

    form.addEventListener('submit', async event => {
        event.preventDefault();
        error.hidden = true;
        if (revealed) {
            conceal();
            return;
        }
        button.disabled = true;
        request = new AbortController();
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                cache: 'no-store',
                signal: request.signal
            });
            const data = await response.json();
            if (!response.ok || typeof data.password !== 'string') {
                error.textContent = data.message || 'The saved password could not be loaded.';
                error.hidden = false;
                return;
            }
            value.textContent = data.password;
            button.textContent = 'Hide password';
            revealed = true;
        } catch (failure) {
            if (failure.name !== 'AbortError') {
                error.textContent = 'The saved password could not be loaded.';
                error.hidden = false;
            }
        } finally {
            request = undefined;
            button.disabled = false;
        }
    });

    window.addEventListener('pagehide', conceal);
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) conceal();
    });
});

document.querySelector('[data-refresh-pppoe]')?.addEventListener('click', event => {
    event.preventDefault();
    history.replaceState(null, '', '#pppoe');
    location.reload();
});
