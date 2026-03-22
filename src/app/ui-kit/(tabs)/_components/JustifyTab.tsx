import React, { useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Col,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  UncontrolledCollapse,
} from "reactstrap";
import {
  IconLifebuoy,
  IconKeyboardShow,
  IconFileDislike,
  IconCode,
} from "@tabler/icons-react";
import classnames from "classnames";

type JustifyTabItem = {
  id: string;
  title: string;
  icon: JSX.Element;
  content: React.ReactNode;
};

const justifyTabs: JustifyTabItem[] = [
  {
    id: "justify-home",
    title: "Home",
    icon: <IconLifebuoy size={24} className="pe-1 ps-1" />,
    content: (
      <p>
        The idea is to use <code>:target</code> pseudoclass to show tabs, use
        anchors with fragment identifiers to switch between them. The idea is to
        use pseudoclass to show tabs, use anchors with fragment identifiers to
        switch between them.
      </p>
    ),
  },
  {
    id: "justify-profile",
    title: "Profile",
    icon: <IconKeyboardShow size={24} className="pe-1 ps-1" />,
    content: (
      <ol>
        <li>Show only the last tab.</li>
        <li>
          If <code>:target</code> matches a tab, show it and hide all following
          siblings.
        </li>
        <li>Matches a tab, show it and hide all following siblings.</li>
      </ol>
    ),
  },
  {
    id: "justify-contact",
    title: "Contact",
    icon: <IconFileDislike size={24} className="pe-1 ps-1" />,
    content: (
      <p>
        The idea is to use <code>:target</code> pseudoclass to show tabs, use
        anchors with fragment identifiers to switch between them. The idea is to
        use pseudoclass to show tabs, use anchors with fragment identifiers to
        switch between them.
      </p>
    ),
  },
];

const JustifyLightTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>(justifyTabs[0]?.id || "");

  return (
    <Col lg={6}>
      <Card>
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5>Justify Light Tabs</h5>
          <a href="#" id="togglerJustifyLightTabBtn">
            <IconCode className="source cursor-pointer" size={32} />
          </a>
        </CardHeader>

        <CardBody>
          <Nav
            tabs
            className="nav-tabs tab-light-secondary justify-content-around"
            id="justify-Light"
            role="tablist"
          >
            {justifyTabs.map((tab) => (
              <NavItem className="flex-fill text-center" key={tab.id}>
                <NavLink
                  className={classnames("w-100", {
                    active: activeTab === tab.id,
                  })}
                  onClick={() => setActiveTab(tab.id)}
                  id={`${tab.id}-tab`}
                  role="tab"
                  aria-controls={`${tab.id}-pane`}
                  aria-selected={activeTab === tab.id}
                >
                  {tab.icon}
                  {tab.title}
                </NavLink>
              </NavItem>
            ))}
          </Nav>

          <TabContent
            activeTab={activeTab}
            className="pt-3"
            id="justify-LightContent"
          >
            {justifyTabs.map((tab) => (
              <TabPane
                tabId={tab.id}
                key={tab.id}
                role="tabpanel"
                id={`${tab.id}-pane`}
                aria-labelledby={`${tab.id}-tab`}
              >
                {tab.content}
              </TabPane>
            ))}
          </TabContent>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerJustifyLightTabBtn">
          <pre>
            <code className="language-html">{`<Card>
  <CardHeader className="d-flex justify-content-between align-items-center">
    <h5>Justify Light Tabs</h5>
    <a href="#justify-LightContent" aria-expanded="false" aria-controls="justify-LightContent">
      <IconCode data-source="t-justify" className="source" size={32} />
    </a>
  </CardHeader>
  <CardBody>
    <Nav tabs className="nav-tabs tab-light-secondary justify-content-around" id="justify-Light" role="tablist">
${justifyTabs
  .map(
    (
      tab
    ) => `      <NavItem className="flex-fill text-center" role="presentation">
        <NavLink
          ${activeTab === tab.id ? 'className="active w-100"' : 'className="w-100"'}
          onClick={() => setActiveTab("${tab.id}")}
          id="${tab.id}-tab"
          type="button"
          role="tab"
          aria-controls="${tab.id}-tab-pane"
          aria-selected="${activeTab === tab.id}"
        >
          <!-- Icon omitted -->
          ${tab.title}
        </NavLink>
      </NavItem>`
  )
  .join("\n")}
    </Nav>
    <TabContent activeTab="${activeTab}" className="pt-3" id="justify-LightContent">
${justifyTabs
  .map(
    (tab) => `      <TabPane
        tabId="${tab.id}"
        role="tabpanel"
        id="${tab.id}-tab-pane"
        aria-labelledby="${tab.id}-tab"
        className="${activeTab === tab.id ? "show active" : ""}"
      >
        ${
          Array.isArray(tab.content)
            ? tab.content.map((c: string) => `        <p>${c}</p>`).join("\n")
            : "        <!-- Content omitted -->"
        }
      </TabPane>`
  )
  .join("\n")}
    </TabContent>
  </CardBody>
</Card>`}</code>
          </pre>
        </UncontrolledCollapse>
      </Card>
    </Col>
  );
};

export default JustifyLightTabs;
