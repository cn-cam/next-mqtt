import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { t } from '../i18n'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/LoginView.vue'),
      meta: { public: true },
    },
    {
      path: '/',
      component: () => import('../components/AppShell.vue'),
      children: [
        {
          path: '',
          name: 'overview',
          component: () => import('../views/OverviewView.vue'),
          meta: { title: 'nav.overview' },
        },
        {
          path: 'clients',
          name: 'clients',
          component: () => import('../views/ClientsView.vue'),
          meta: { title: 'nav.clients' },
        },
        {
          path: 'subscriptions',
          name: 'subscriptions',
          component: () => import('../views/SubscriptionsView.vue'),
          meta: { title: 'nav.subscriptions' },
        },
        {
          path: 'retained',
          name: 'retained',
          component: () => import('../views/RetainedView.vue'),
          meta: { title: 'nav.retained' },
        },
        {
          path: 'publish',
          name: 'publish',
          component: () => import('../views/PublishView.vue'),
          meta: { title: 'nav.publish' },
        },
        {
          path: 'cluster',
          name: 'cluster',
          component: () => import('../views/ClusterView.vue'),
          meta: { title: 'nav.cluster' },
        },
        {
          path: 'settings',
          name: 'settings',
          component: () => import('../views/SettingsView.vue'),
          meta: { title: 'nav.settings' },
        },
      ],
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('../views/NotFoundView.vue'),
      meta: { public: true },
    },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()

  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.name === 'login' && auth.isAuthenticated) {
    return { name: 'overview' }
  }

  return true
})

router.afterEach((to) => {
  const title = typeof to.meta.title === 'string' ? t(to.meta.title) : undefined
  document.title = title ? `${title} · NextMQTT` : 'NextMQTT'
})

export default router
