// Desativado por decisão de design: o VFX de Dodge agora é disparado por código
// diretamente em NitssMovementController.HandleDash(), sem depender do Animator.
// Mantemos o arquivo para evitar referências quebradas no Animator, porém o código
// está deliberadamente excluído da build.
#if false
using UnityEngine;

namespace Hazze.Gameplay.Combat
{
    /// <summary>
    /// StateMachineBehaviour para disparar o VFX de Dodge ao entrar no estado.
    /// </summary>
    public class DodgeVFXBehaviour : StateMachineBehaviour
    {
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!animator) return;
            var vfx = animator.GetComponentInParent<AttackSlashVFX>() ?? animator.GetComponent<AttackSlashVFX>();
            if (vfx != null)
            {
                vfx.TriggerDodgeVFX();
            }
        }
    }
}
#endif
