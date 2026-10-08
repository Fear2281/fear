"""Optional browser checks; use after starting Encore on port 5080.
Requires: pip install playwright; Chromium (or playwright install chromium).
Creates demo requests through the interface. Run with a disposable data directory.
"""
from pathlib import Path
import shutil
from playwright.sync_api import sync_playwright, expect
ROOT=Path(__file__).resolve().parents[1]
IMAGES=ROOT/'docs/images'
IMAGES.mkdir(parents=True,exist_ok=True)
with sync_playwright() as p:
    browser=p.chromium.launch(executable_path=shutil.which('chromium'),headless=True,args=['--no-sandbox'])
    context=browser.new_context(viewport={'width':1440,'height':1100},device_scale_factor=1)
    page=context.new_page()
    errors=[]
    page.on('pageerror',lambda e:errors.append(str(e)))
    page.goto('http://localhost:5080')
    page.get_by_role('heading',name='Что споём сегодня?').wait_for()
    page.locator('.track').first.wait_for()
    assert page.locator('.track').count()==11
    page.get_by_role('button',name='Подключить столик').click()
    page.get_by_role('button',name='Попробовать за столиком').click()
    page.get_by_role('button',name='Заказать Fly Me to the Moon',exact=True).click()
    expect(page.locator('#toast')).to_contain_text('отправлена диджею')
    page.locator('#guest-queue').get_by_text('Fly Me to the Moon').wait_for()
    page.get_by_role('button',name='Заказать Feeling Good',exact=True).click()
    expect(page.locator('#toast')).to_contain_text('отправлена диджею')
    page.locator('#guest-queue').get_by_text('Feeling Good').wait_for()
    page.locator('#search').fill('sinatra')
    expect(page.locator('.track')).to_have_count(1)
    page.locator('#search').fill('')
    expect(page.locator('.track')).to_have_count(11)
    page.get_by_role('button',name='Пульт диджея').click()
    page.locator('#password').fill('encore-demo')
    page.get_by_role('button',name='Войти в пульт').click()
    page.locator('#dj-dashboard').wait_for(state='visible')
    tables=page.request.get('http://localhost:5080/api/dj/tables').json()
    second=browser.new_context()
    g2=second.new_page()
    g2.goto('http://localhost:5080/table/'+tables[1]['token'])
    g2.get_by_role('button',name='Заказать City of Stars',exact=True).click()
    g2.locator('#guest-queue').get_by_text('City of Stars').wait_for()
    third=browser.new_context()
    g3=third.new_page()
    g3.goto('http://localhost:5080/table/'+tables[2]['token'])
    g3.get_by_role('button',name='Заказать Valerie',exact=True).click()
    g3.locator('#guest-queue').get_by_text('Valerie').wait_for()
    g2.get_by_role('button',name='Поддержать Feeling Good',exact=True).click()
    page.locator('#dj-queue .dj-card').filter(has_text='Fly Me to the Moon').get_by_role('button',name='Одобрить').click()
    page.locator('#dj-queue .dj-card').filter(has_text='Fly Me to the Moon').get_by_role('button',name='На сцену').click()
    page.locator('#dj-playing').get_by_text('Fly Me to the Moon').wait_for()
    page.locator('#dj-queue .dj-card').filter(has_text='Feeling Good').get_by_role('button',name='Одобрить').click()
    page.wait_for_timeout(500)
    page.screenshot(path=str(IMAGES/'dj-desktop.png'),full_page=True)
    page.get_by_role('button',name='Для гостей',exact=True).click()
    page.wait_for_timeout(3200)
    page.locator('#toast').evaluate('(e)=>e.hidden=true')
    page.screenshot(path=str(IMAGES/'guest-desktop.png'),full_page=True)
    page.set_viewport_size({'width':390,'height':844})
    page.screenshot(path=str(IMAGES/'guest-mobile.png'),full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mobile horizontal overflow'
    page.get_by_role('button',name='Пульт диджея').click()
    page.locator('#dj-dashboard').wait_for(state='visible')
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'DJ mobile horizontal overflow'
    page.screenshot(path=str(IMAGES/'dj-mobile.png'),full_page=True)
    assert not errors,errors
    browser.close()
    print('PASS: browser ordering, search, DJ login, voting and moderation; mobile layouts 390px; no JavaScript errors.')
