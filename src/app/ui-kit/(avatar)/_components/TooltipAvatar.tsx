import {
  Col,
  Card,
  CardHeader,
  CardBody,
  UncontrolledTooltip,
  UncontrolledCollapse,
} from "reactstrap";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faUser } from "@fortawesome/free-solid-svg-icons";
import { IconCode } from "@tabler/icons-react";

const avatarGroups = [
  {
    tooltipAvatars: [
      {
        id: "Tooltip1",
        src: "/images/avatar/4.png",
        tooltip: "Hello from Tooltip 1!",
        bg: "primary",
      },
      {
        id: "Tooltip2",
        src: "/images/avatar/5.png",
        tooltip: "Hello from Tooltip 2!",
        bg: "success",
      },
      {
        id: "Tooltip3",
        src: "/images/avatar/6.png",
        tooltip: "Hello from Tooltip 3!",
        bg: "danger",
      },
    ],
  },
  {
    tooltipIcons: [
      {
        id: "Tooltip4",
        icon: faUser,
        tooltip: "Hello from Tooltip 4!",
        bg: "primary",
      },
      {
        id: "Tooltip5",
        icon: faUser,
        tooltip: "Hello from Tooltip 5!",
        bg: "secondary",
      },
      {
        id: "Tooltip6",
        icon: faUser,
        tooltip: "Hello from Tooltip 6!",
        bg: "success",
      },
    ],
  },
  {
    tooltipText: [
      {
        id: "Tooltip7",
        text: "A",
        tooltip: "Hello from Tooltip 7!",
        bg: "danger",
      },
      {
        id: "Tooltip8",
        text: "CD",
        tooltip: "Hello from Tooltip 8!",
        bg: "dark",
      },
      {
        id: "Tooltip9",
        text: "XYZ",
        tooltip: "Hello from Tooltip 9!",
        bg: "warning",
      },
      {
        id: "Tooltip10",
        text: "2+",
        tooltip: "2 more",
        bg: "secondary",
        size: "30",
      },
    ],
  },
];

const AvatarGroupWithTooltip = () => {
  return (
    <Col md={7}>
      <Card>
        <CardHeader className="code-header">
          <h5>Group with Tooltip</h5>
          <a href="#" id="togglerTooltipBtn">
            <IconCode data-source="av7" className="source" size={32} />
          </a>
          <p className="text-muted">User group with tooltip</p>
        </CardHeader>

        <CardBody>
          <div className="d-flex gap-3 flex-wrap">
            {avatarGroups.map((group, groupIndex) => (
              <ul className="avatar-group" key={groupIndex}>
                {group.tooltipAvatars?.map((a) => (
                  <li
                    key={a.id}
                    id={a.id}
                    className={`h-45 w-45 d-flex-center b-r-50 overflow-hidden text-bg-${a.bg}`}
                  >
                    <img src={a.src} alt="" className="img-fluid" />
                    <UncontrolledTooltip target={a.id} placement="top">
                      {a.tooltip}
                    </UncontrolledTooltip>
                  </li>
                ))}

                {group.tooltipIcons?.map((a) => (
                  <li
                    key={a.id}
                    id={a.id}
                    className={`h-45 w-45 d-flex-center b-r-50 text-bg-${a.bg}`}
                  >
                    <FontAwesomeIcon icon={a.icon} />
                    <UncontrolledTooltip target={a.id} placement="top">
                      {a.tooltip}
                    </UncontrolledTooltip>
                  </li>
                ))}

                {group.tooltipText?.map((a) => (
                  <li
                    key={a.id}
                    id={a.id}
                    className={`h-${a.size || 45} w-${a.size || 45} d-flex-center b-r-50 text-bg-${a.bg}`}
                  >
                    {a.text}
                    <UncontrolledTooltip target={a.id} placement="top">
                      {a.tooltip}
                    </UncontrolledTooltip>
                  </li>
                ))}
              </ul>
            ))}
          </div>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerTooltipBtn">
          <pre>
            <code className="language-html">
              {`<div className="d-flex gap-3 flex-wrap">
${avatarGroups
  .map((group) => {
    if (group.tooltipAvatars) {
      return `  <ul className="avatar-group">
${group.tooltipAvatars
  .map(
    (
      a
    ) => `    <li className="h-45 w-45 d-flex-center b-r-50 overflow-hidden text-bg-${a.bg}" ">
      <img src="${a.src}" alt="" className="img-fluid">
    </li>`
  )
  .join("\n")}
  </ul>`;
    }

    if (group.tooltipIcons) {
      return `  <ul className="avatar-group">
${group.tooltipIcons
  .map(
    (a) => `    <li className="text-bg-${a.bg} h-45 w-45 d-flex-center b-r-50">
      &lt;i className="fa fa-user"&gt;&lt;/i&gt;
    </li>`
  )
  .join("\n")}
  </ul>`;
    }

    if (group.tooltipText) {
      return `  <ul className="avatar-group">
${group.tooltipText
  .map(
    (
      a
    ) => `    <li className="text-bg-${a.bg} h-${a.size || 45} w-${a.size || 45} d-flex-center b-r-50" >
      ${a.text}
    </li>`
  )
  .join("\n")}
  </ul>`;
    }

    return "";
  })
  .join("\n\n")}
</div>`}
            </code>
          </pre>
        </UncontrolledCollapse>
      </Card>
    </Col>
  );
};

export default AvatarGroupWithTooltip;
