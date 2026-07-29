const modal = document.querySelector('#sample-modal');
const openButton = document.querySelector('#open-modal');
const closeButton = document.querySelector('#close-modal');

openButton.addEventListener('click', () => modal.showModal());
closeButton.addEventListener('click', () => modal.close());
modal.addEventListener('click', (event) => {
  if (event.target === modal) modal.close();
});
