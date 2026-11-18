const toggle = document.querySelector('[data-theme-toggle]');
const phone = document.querySelector('.mockup-phone');

function setTheme(dark) {
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  if (toggle) toggle.textContent = dark ? 'Light mode' : 'Dark mode';
  if (phone) phone.src = 'member-board.html?theme=' + (dark ? 'dark' : 'light');
}

setTheme(new URLSearchParams(location.search).get('theme') === 'dark');
toggle?.addEventListener('click', () => setTheme(document.documentElement.dataset.theme !== 'dark'));
