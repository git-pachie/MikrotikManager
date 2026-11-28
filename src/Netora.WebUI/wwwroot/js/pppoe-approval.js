const approvalDialog = document.getElementById('pppoe-approval');
const approvalProfile = document.getElementById('approval-profile');
const localIp = document.getElementById('approval-local-ip');
const confirmApproval = document.getElementById('confirm-pppoe-approval');

function showProfileLocalIp() {
    const defaultIp = approvalProfile.selectedOptions[0]?.dataset.local;
    localIp.placeholder = defaultIp ? `Profile default: ${defaultIp}` : 'Use profile default';
}

function openApproval() {
    approvalDialog.showModal();
    confirmApproval.disabled = false;
}

approvalProfile.addEventListener('change', showProfileLocalIp);
document.getElementById('open-pppoe-approval').addEventListener('click', openApproval);
document.getElementById('close-pppoe-approval').addEventListener('click', () => approvalDialog.close());
approvalDialog.addEventListener('close', () => {
    confirmApproval.disabled = true;
    document.getElementById('approval-pppoe-password').value = '';
});
showProfileLocalIp();
if (approvalDialog.dataset.reopen === 'true') openApproval();
