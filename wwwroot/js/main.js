// main.js - control de pestañas, sesión, clima y contacto

document.addEventListener('DOMContentLoaded', () => {
    // Verificar sesión
    verificarSesion();

    // Pestañas
    document.querySelectorAll('#mainTabs .nav-link').forEach(link => {
        link.addEventListener('click', (e) => {
            e.preventDefault();
            const tab = link.getAttribute('data-tab');
            switchTab(tab);
            document.querySelectorAll('#mainTabs .nav-link').forEach(l => l.classList.remove('active'));
            link.classList.add('active');
        });
    });

    // Logout
    document.getElementById('logoutBtn').addEventListener('click', async () => {
        await fetch('/api/logout', { method: 'POST' });
        window.location.href = '/login.html';
    });

    // Buscar clima
    document.getElementById('searchBtn').addEventListener('click', () => {
        const city = document.getElementById('cityInput').value.trim();
        if (city) fetchClima(city);
    });

    // Quick cities
    document.querySelectorAll('.quick-city').forEach(btn => {
        btn.addEventListener('click', () => fetchClima(btn.dataset.city));
    });

    // Contact form
    document.getElementById('contactForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        const payload = {
            nombre: document.getElementById('nombre').value.trim(),
            email: document.getElementById('email').value.trim(),
            ciudad: document.getElementById('ciudad').value,
            mensaje: document.getElementById('mensaje').value.trim(),
            newsletter: document.getElementById('newsletter').checked
        };

        // Validación simple
        if (!payload.nombre || !payload.email || payload.mensaje.length < 10) {
            alert('Completa correctamente los campos requeridos.');
            return;
        }

        const resp = await fetch('/api/contacto', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (resp.ok) {
            alert('Mensaje enviado correctamente, te responderemos en 24h');
            document.getElementById('contactForm').reset();
        } else {
            const data = await resp.json();
            alert(data?.message || 'Error al enviar mensaje');
        }
    });

    // Inicializar gráfico vacío
    initChart();
});

// Cambiar pestañas
function switchTab(tab) {
    document.querySelectorAll('.tab-content .tab-pane').forEach(p => {
        p.style.display = (p.id === tab) ? '' : 'none';
        p.classList.toggle('active', p.id === tab);
    });
}

// Verificar sesión en backend
async function verificarSesion() {
    const resp = await fetch('/api/verificar-sesion');
    const data = await resp.json();
    if (!data.authenticated) {
        window.location.href = '/login.html';
        return;
    }
    document.getElementById('welcomeText').textContent = `Bienvenido, ${data.email}`;
}

// Fetch clima
async function fetchClima(ciudad) {
    showLoader(true);
    try {
        const resp = await fetch(`/api/clima?ciudad=${encodeURIComponent(ciudad)}`);
        if (!resp.ok) {
            const err = await resp.json();
            showWeatherError(err?.message || 'Ciudad no encontrada');
            showLoader(false);
            return;
        }
        const data = await resp.json();
        renderWeather(data);
        updateChartWithSimulatedTemps(data.temperatura);
    } catch (e) {
        showWeatherError('Error al obtener datos de clima');
    } finally {
        showLoader(false);
    }
}

function showLoader(show) {
    const card = document.getElementById('weatherCard');
    if (show) {
        card.style.display = '';
        card.innerHTML = '<div class="text-center p-4">Cargando...</div>';
    }
}

function showWeatherError(msg) {
    const card = document.getElementById('weatherCard');
    card.style.display = '';
    card.innerHTML = `<div class="text-danger p-3">${msg}</div>`;
}

function renderWeather(d) {
    const icon = mapIconToEmoji(d.icon, d.descripcion);
    const html = `
    <div class="d-flex justify-content-between align-items-center">
      <div>
        <h4 class="mb-0">${d.ciudad}, ${d.pais}</h4>
        <div class="text-muted">${d.descripcion}</div>
      </div>
      <div class="text-end">
        <div class="temp">${Math.round(d.temperatura)}°C</div>
        <div class="small text-muted">Sensación ${Math.round(d.sensacion)}°C</div>
      </div>
    </div>
    <hr/>
    <div class="d-flex justify-content-between">
      <div>Humedad: <strong>${d.humedad}%</strong></div>
      <div>Viento: <strong>${d.viento} m/s</strong></div>
      <div>Presión: <strong>${d.presion} hPa</strong></div>
    </div>
    <div class="mt-3 text-center fs-1">${icon}</div>
  `;
    document.getElementById('weatherContent').innerHTML = html;
}

// Mapear icon code a emoji simple
function mapIconToEmoji(icon, desc) {
    if (!icon) return '🌤️';
    if (icon.startsWith('01')) return '☀️';
    if (icon.startsWith('02') || icon.startsWith('03') || icon.startsWith('04')) return '⛅';
    if (icon.startsWith('09') || icon.startsWith('10')) return '🌧️';
    if (icon.startsWith('11')) return '⛈️';
    if (icon.startsWith('13')) return '❄️';
    if (icon.startsWith('50')) return '🌫️';
    return '🌤️';
}

/* Chart.js */
let chart;
function initChart() {
    const ctx = document.getElementById('tempChart').getContext('2d');
    chart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: ['+1h', '+2h', '+3h', '+4h', '+5h'],
            datasets: [{
                label: 'Temperatura °C',
                data: [0, 0, 0, 0, 0],
                borderColor: '#dc3545',
                backgroundColor: 'rgba(220,53,69,0.2)',
                tension: 0.3
            }]
        },
        options: {
            responsive: true,
            scales: { y: { beginAtZero: false } }
        }
    });
}

function updateChartWithSimulatedTemps(baseTemp) {
    const temps = [];
    for (let i = 1; i <= 5; i++) {
        const variation = (Math.random() * 4 - 2); // -2 a +2
        temps.push(Math.round(baseTemp + variation));
    }
    chart.data.datasets[0].data = temps;
    chart.update();
}
